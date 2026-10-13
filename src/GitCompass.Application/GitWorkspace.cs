using System.Collections.Concurrent;
using System.Collections.Immutable;
using GitCompass.Domain;

namespace GitCompass.Application;

public interface IGitBackend
{
    event Action<OperationEvent>? Activity;
    Task<ProjectRegistration> DiscoverAsync(string selectedFolder, CancellationToken cancellation);
    Task<RepositorySnapshot> InspectAsync(ProjectRegistration project, long generation, RemoteObservation remote, CancellationToken cancellation);
    Task<HistoryPage> HistoryAsync(RepositorySnapshot snapshot, int skip, int take, CancellationToken cancellation);
    Task<HistoryPage> HistoryAsync(RepositorySnapshot snapshot, HistoryRequest request, CancellationToken cancellation);
    Task<ImmutableArray<BranchInfo>> BranchesAsync(RepositorySnapshot snapshot, CancellationToken cancellation);
    Task<DiffResult> DiffAsync(RepositorySnapshot snapshot, DiffRequest request, CancellationToken cancellation);
    Task<ImmutableArray<IgnoreSuggestion>> IgnoreSuggestionsAsync(RepositorySnapshot snapshot, CancellationToken cancellation);
    Task<OperationPreview> PlanAsync(OperationRequest request, RepositorySnapshot snapshot, AccessGrant grant, CancellationToken cancellation);
    Task<BackendResult> ExecuteAsync(OperationPreview preview, RepositorySnapshot snapshot, CancellationToken cancellation);
}
public sealed record BackendResult(OperationState State, int? ExitCode, string Summary, string Output, string Error, string? StashId = null, bool Truncated = false);

public sealed class GitWorkspace(IGitBackend backend)
{
    private readonly ConcurrentDictionary<Guid, ProjectRegistration> projects = new();
    private readonly ConcurrentDictionary<Guid, RepositorySnapshot> snapshots = new();
    private readonly ConcurrentDictionary<Guid, RemoteObservation> remotes = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, OperationPreview> previews = new();
    private long generation;
    public AccessRegistry Access { get; } = new();
    public IReadOnlyList<ProjectRegistration> Projects => projects.Values.OrderBy(p => p.DisplayName).ToArray();
    public event Action<OperationEvent>? Activity { add => backend.Activity += value; remove => backend.Activity -= value; }
    public event Action<RepositorySnapshot>? SnapshotChanged;
    public async Task<RepositorySnapshot> OpenAsync(string folder, CancellationToken cancellation = default)
    {
        var discovered = await backend.DiscoverAsync(folder, cancellation);
        var existing = projects.Values.FirstOrDefault(p => string.Equals(p.RootPath, discovered.RootPath, StringComparison.OrdinalIgnoreCase));
        var project = existing ?? discovered; projects[project.Id] = project; Access.Register(project.Id, repositoryIdentity: project.CommonGitDirectory.Length == 0 ? project.RootPath : project.CommonGitDirectory);
        return await RefreshAsync(project.Id, cancellation);
    }
    public ProjectRegistration SetTrusted(Guid projectId, bool trusted)
    {
        var project = Project(projectId) with { Trusted = trusted }; projects[projectId] = project; Invalidate(projectId); return project;
    }
    public ProjectRegistration SetMonitoring(Guid projectId, bool enabled)
    {
        var project = Project(projectId) with { MonitoringEnabled = enabled }; projects[projectId] = project; return project;
    }
    public ProjectRegistration SetIntendedBranch(Guid projectId, string? branch)
    {
        var project = Project(projectId) with { IntendedBranch = string.IsNullOrWhiteSpace(branch) ? null : branch }; projects[projectId] = project; Invalidate(projectId); return project;
    }
    public RepositorySnapshot? GetSnapshot(Guid projectId) => snapshots.GetValueOrDefault(projectId);
    public async Task<RepositorySnapshot> RefreshAsync(Guid projectId, CancellationToken cancellation = default)
    {
        var snapshot = await backend.InspectAsync(Project(projectId), Interlocked.Increment(ref generation), remotes.GetValueOrDefault(projectId) ?? new(RemoteCheckState.NotChecked), cancellation);
        projects[projectId] = snapshot.Project;
        Access.Register(projectId, repositoryIdentity: snapshot.Project.CommonGitDirectory.Length == 0 ? snapshot.Project.RootPath : snapshot.Project.CommonGitDirectory);
        snapshots[projectId] = snapshot; SnapshotChanged?.Invoke(snapshot); return snapshot;
    }
    public RecommendationPlan Plan(Guid projectId, UserGoal goal = UserGoal.Understand) => RecommendationEngine.Build(Snapshot(projectId), goal);
    public Task<HistoryPage> HistoryAsync(Guid projectId, int skip = 0, int take = 200, CancellationToken cancellation = default) => backend.HistoryAsync(Snapshot(projectId), Math.Max(0, skip), Math.Clamp(take, 1, 500), cancellation);
    public Task<HistoryPage> HistoryAsync(Guid projectId, HistoryRequest request, CancellationToken cancellation = default) => backend.HistoryAsync(Snapshot(projectId), request with { Skip = Math.Max(0, request.Skip), Take = Math.Clamp(request.Take, 1, 500) }, cancellation);
    public Task<ImmutableArray<BranchInfo>> BranchesAsync(Guid projectId, CancellationToken cancellation = default) => backend.BranchesAsync(Snapshot(projectId), cancellation);
    public Task<DiffResult> DiffAsync(Guid projectId, DiffRequest request, CancellationToken cancellation = default) => backend.DiffAsync(Snapshot(projectId), request, cancellation);
    public Task<ImmutableArray<IgnoreSuggestion>> IgnoreSuggestionsAsync(Guid projectId, CancellationToken cancellation = default) => backend.IgnoreSuggestionsAsync(Snapshot(projectId), cancellation);
    public async Task<OperationPreview> PreviewAsync(OperationRequest request, CancellationToken cancellation = default)
    {
        Access.Authorize(request.ProjectId, request.Kind);
        var snapshot = await RefreshAsync(request.ProjectId, cancellation); var grant = Access.Get(request.ProjectId);
        Access.Authorize(request.ProjectId, request.Kind, grant.Generation);
        using var lease = Access.Acquire(request.ProjectId, request.Kind, grant.Generation);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lease.Revoked);
        var preview = await backend.PlanAsync(request, snapshot, grant, linked.Token);
        Access.Authorize(request.ProjectId, request.Kind, grant.Generation);
        previews[preview.Id] = preview; return preview;
    }
    public async Task<OperationResult> ExecuteAsync(OperationPreview preview, CancellationToken cancellation = default)
    {
        if (!previews.TryRemove(preview.Id, out var issued) || issued != preview) throw new StalePreviewException("This preview was not issued by this workspace or was already used.");
        var project = Project(preview.Request.ProjectId); Access.Authorize(project.Id, preview.Request.Kind, preview.AccessGeneration);
        var gate = locks.GetOrAdd(project.CommonGitDirectory.Length == 0 ? project.RootPath : project.CommonGitDirectory, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(cancellation);
        try
        {
            if (projects.Values.Any(p => p.CommonGitDirectory == project.CommonGitDirectory && Access.HasActive(p.Id, OperationKind.Terminal))) throw new AccessDeniedException("Close active terminal sessions sharing this repository's Git metadata before a managed operation.");
            using var lease = Access.Acquire(project.Id, preview.Request.Kind, preview.AccessGeneration);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lease.Revoked);
            var before = await RefreshAsync(project.Id, linked.Token);
            if (before.Fingerprint != preview.Fingerprint) throw new StalePreviewException("The branch, index, selected file contents or configuration changed after review. Refresh and create a new preview.");
            Access.Authorize(project.Id, preview.Request.Kind, preview.AccessGeneration);
            var result = await backend.ExecuteAsync(preview, before, linked.Token);
            if (preview.Request.Kind == OperationKind.Fetch)
            {
                var old = remotes.GetValueOrDefault(project.Id) ?? new(RemoteCheckState.NotChecked);
                remotes[project.Id] = result.State == OperationState.Succeeded ? new(RemoteCheckState.Succeeded, DateTimeOffset.UtcNow, preview.Request.Remote ?? before.Push?.Remote) : old with { State = result.State == OperationState.OutcomeUnknown ? RemoteCheckState.Unknown : RemoteCheckState.Failed, Error = result.Summary };
            }
            var after = await RefreshAsync(project.Id, CancellationToken.None);
            return new(preview.Id, result.State, result.ExitCode, result.Summary, result.Output, result.Error, after, result.StashId, result.Truncated);
        }
        finally { gate.Release(); }
    }
    private ProjectRegistration Project(Guid id) => projects.TryGetValue(id, out var project) ? project : throw new InvalidOperationException("Open the project first.");
    private RepositorySnapshot Snapshot(Guid id) => snapshots.TryGetValue(id, out var snapshot) ? snapshot : throw new InvalidOperationException("Inspect the project first.");
    private void Invalidate(Guid id) { foreach (var pair in previews.Where(p => p.Value.Request.ProjectId == id)) previews.TryRemove(pair.Key, out _); }
}
