using System.Collections.Immutable;
using GitCompass.Application;
using GitCompass.Domain;

var count = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAILED: " + label); Console.WriteLine($"PASS {++count}: {label}"); }
void Denied(Action action, string label) { try { action(); throw new Exception("Permission unexpectedly allowed: " + label); } catch (AccessDeniedException) { Check(true, label); } }
var project = new ProjectRegistration(Guid.NewGuid(), "Practice", "folder", "folder", "common", RepositoryKind.WorkingTree);
RepositorySnapshot Snapshot(int? ahead = 0, int? behind = 0, ImmutableArray<FileState> files = default, GitOperation operation = GitOperation.None) => new(project.Id, 1, DateTimeOffset.UtcNow, project, "abcd", "main", BranchState.Attached, "team/main", "bcde", new("team", "https://example.test/repo", "refs/heads/main", "refs/heads/main"), files.IsDefault ? [] : files, operation, ahead, behind, false, new(RemoteCheckState.Succeeded, DateTimeOffset.UtcNow), "fingerprint", "test", []);
var behindDirty = Snapshot(0, 2, [new("a", null, '.', 'M', FileKind.Tracked)]);
var plan = RecommendationEngine.Build(behindDirty);
Check(plan.Steps[0].Code == "preserve-work" && plan.Steps.Any(s => s.Code == "behind"), "Local preservation precedes incoming review and preserves both facts");
Check(RecommendationEngine.Build(Snapshot(2, 2)).Steps[0].Code == "diverged", "Divergence uses ancestry counts");
Check(RecommendationEngine.Build(Snapshot(operation: GitOperation.Merge)).Steps[0].Code == "operation-recovery", "Interrupted operation takes priority");
Check(!RecommendationEngine.Build(Snapshot()).Steps.Any(s => s.Code == "branch-intent"), "No branch mismatch is invented when intent is unset");
Check(RecommendationEngine.Build(Snapshot() with { Project = project with { IntendedBranch = "task" } }).Steps[0].Code == "branch-intent", "Explicit branch intent mismatch is actionable");
Check(!RecommendationEngine.Build(Snapshot(null, null) with { BranchState = BranchState.Unborn, HeadId = null, UpstreamId = null }).Steps.Any(s => s.Code is "known-aligned" or "ahead" or "behind"), "Unknown/unborn comparison never becomes zero");
Check(RecommendationEngine.Build(Snapshot() with { BranchState = BranchState.Detached, Branch = null }).Steps[0].Code == "detached", "Detached work suppresses ordinary branch publication");
Check(RecommendationEngine.Build(Snapshot() with { UpstreamId = null, Ahead = null, Behind = null }).Steps.Any(s => s.Code == "upstream-missing"), "Removed upstream stays explicitly unknown");
Check(RecommendationEngine.Build(Snapshot() with { IsShallow = true }).Steps.Any(s => s.Code == "shallow"), "Limited history is explained");
Check(RecommendationEngine.Build(Snapshot() with { Remote = new(RemoteCheckState.Failed, DateTimeOffset.UtcNow.AddDays(-1), "team", "offline") }).Steps.Any(s => s.Code == "remote-stale"), "Failed remote check keeps stale evidence visible");
Check(RecommendationEngine.Build(Snapshot(2, 0), UserGoal.GetLatest).Steps.All(s => s.Action != OperationKind.Push), "Get latest retains outgoing facts without recommending an unrequested upload");
Check(RecommendationEngine.Build(Snapshot(), UserGoal.StartTask).Steps.Any(s => s.Code == "start-task" && s.Action == OperationKind.CreateBranch) && !RecommendationEngine.Build(Snapshot(), UserGoal.StartTask).Steps.Any(s => s.Code == "branch-intent"), "Starting a task asks for an explicit branch/base without inventing intent");
Check(RecommendationEngine.Build(Snapshot(), UserGoal.PreparePullRequest).Steps.Any(s => s.Code == "prepare-pr" && s.Lesson == "pr"), "PR goal explains chosen hosted source/target after publication prerequisites");
var access = new AccessRegistry(); access.Register(project.Id);
Check(access.Get(project.Id).Level == AccessLevel.ReadOnly, "New projects begin Read-only");
foreach (var kind in Enum.GetValues<OperationKind>().Except([OperationKind.Inspect])) Denied(() => access.Authorize(project.Id, kind), "Read-only blocks " + kind);
access.Authorize(project.Id, OperationKind.Inspect); Check(true, "Read-only allows truthful local inspection capability");
access.SetLevel(project.Id, AccessLevel.MonitorOnly); access.Authorize(project.Id, OperationKind.Fetch); access.Authorize(project.Id, OperationKind.GitHubRead);
Check(true, "Monitor permits only fetch and hosted reads");
foreach (var kind in Enum.GetValues<OperationKind>().Except([OperationKind.Fetch, OperationKind.GitHubRead, OperationKind.Inspect])) Denied(() => access.Authorize(project.Id, kind), "Monitor blocks " + kind);
access.SetLevel(project.Id, AccessLevel.AllowChanges); var generation = access.Get(project.Id).Generation;
using (var lease = access.Acquire(project.Id, OperationKind.Terminal, generation))
{
    var grant = access.SetLevel(project.Id, AccessLevel.ReadOnly);
    Check(grant.Level == AccessLevel.AllowChanges && grant.PendingLevel == AccessLevel.ReadOnly, "Downgrade is pending while a terminal lease owns activity");
    Check(lease.Revoked.IsCancellationRequested, "Downgrade asks active owned activity to cancel");
    Denied(() => access.Authorize(project.Id, OperationKind.Terminal), "Pending downgrade denies new input");
    Denied(() => access.Authorize(project.Id, OperationKind.Stage, generation), "Queued old grant is immediately revoked");
}
Check(access.Get(project.Id).Level == AccessLevel.ReadOnly && access.Get(project.Id).PendingLevel == null, "Read-only becomes active after owned activity is closed");
var restarted = new AccessRegistry(); restarted.Register(project.Id);
Check(restarted.Get(project.Id).Level == AccessLevel.ReadOnly && restarted.SessionId != access.SessionId, "Restart does not preserve unrestricted access");
var linkedId = Guid.NewGuid(); access.Register(linkedId, repositoryIdentity: "same-common"); access.Register(project.Id, repositoryIdentity: "same-common");
access.SetLevel(project.Id, AccessLevel.AllowChanges); access.SetLevel(linkedId, AccessLevel.AllowChanges);
using (var lease = access.Acquire(project.Id, OperationKind.Commit)) Denied(() => access.Acquire(linkedId, OperationKind.Terminal), "Shared-worktree managed lease atomically blocks another project's terminal");
using (var lease = access.Acquire(linkedId, OperationKind.Terminal)) Denied(() => access.Acquire(project.Id, OperationKind.Fetch), "Shared-worktree terminal lease atomically blocks another project's metadata operation");
Console.WriteLine($"{count} domain assertions passed.");
