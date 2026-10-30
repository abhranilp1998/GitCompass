using GitCompass.Application;
using GitCompass.Domain;

namespace GitCompass.Infrastructure.Local;

public sealed record MonitoringNotice(Guid ProjectId, string Message);
/// <summary>Visible opt-in monitoring while the app is running. All work uses the ordinary coordinator.</summary>
public sealed class RemoteCheckScheduler(GitWorkspace workspace, TimeSpan interval, Func<DateTimeOffset, bool>? mayNotify = null) : IAsyncDisposable
{
    private sealed class Schedule { public DateTimeOffset Next; public int Failures; public string? LastMeaningful; public bool PausedForAuthentication; }
    private readonly Dictionary<Guid, Schedule> _schedules = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    public event Action<MonitoringNotice>? Notice;
    public event Action<Exception>? Fault;
    private void PublishNotice(MonitoringNotice notice)
    {
        if (mayNotify?.Invoke(DateTimeOffset.Now) != false) Notice?.Invoke(notice);
    }
    public void Start() => _worker ??= RunAsync(_stop.Token);
    public void Resume(Guid projectId)
    {
        lock (_schedules) if (_schedules.TryGetValue(projectId, out var schedule))
        { schedule.PausedForAuthentication = false; schedule.Failures = 0; schedule.Next = DateTimeOffset.UtcNow; }
    }
    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            foreach (var project in workspace.Projects)
            {
                if (!project.MonitoringEnabled || !project.Trusted || project.Kind is not (RepositoryKind.WorkingTree or RepositoryKind.LinkedWorktree)) continue;
                var grant = workspace.Access.Get(project.Id);
                if (grant.Level < AccessLevel.MonitorOnly || grant.PendingLevel.HasValue) continue;
                Schedule schedule;
                lock (_schedules)
                {
                    if (!_schedules.TryGetValue(project.Id, out schedule!))
                        _schedules[project.Id] = schedule = new Schedule { Next = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Random.Shared.Next(5, 20)) };
                }
                if (schedule.PausedForAuthentication || schedule.Next > DateTimeOffset.UtcNow) continue;
                try
                {
                    var before = workspace.GetSnapshot(project.Id);
                    var preview = await workspace.PreviewAsync(new(project.Id, OperationKind.Fetch), token);
                    var result = await workspace.ExecuteAsync(preview, token);
                    if (result.State != OperationState.Succeeded)
                    {
                        schedule.Failures++;
                        var error = result.Error + " " + result.Summary;
                        schedule.PausedForAuthentication = error.Contains("authentication", StringComparison.OrdinalIgnoreCase) || error.Contains("credential", StringComparison.OrdinalIgnoreCase);
                        if (schedule.Failures == 1 || schedule.PausedForAuthentication)
                            PublishNotice(new(project.Id, schedule.PausedForAuthentication
                                ? "Remote monitoring paused: sign in through Git, then resume checks."
                                : "Remote check failed. Cached facts remain visible; checks will slow down."));
                    }
                    else
                    {
                        schedule.Failures = 0;
                        var after = result.Snapshot;
                        var meaningful = $"{after.Branch}|{after.UpstreamId}|{after.Ahead}|{after.Behind}";
                        if (schedule.LastMeaningful != meaningful &&
                            (after.Behind > (before?.Behind ?? 0) || after.Ahead > 0 && after.Behind > 0 || before?.UpstreamId != null && after.UpstreamId == null))
                            PublishNotice(new(project.Id, after.Ahead > 0 && after.Behind > 0
                                ? "Local and team history have both changed. See ways to combine work (merge)."
                                : after.UpstreamId == null ? "The tracked remote branch is missing. Review the destination." : $"{after.Behind} team commits are ready to review."));
                        schedule.LastMeaningful = meaningful;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (AccessDeniedException) { /* Permission revocation is expected and prevents queued work. */ }
                catch (StalePreviewException) { /* Re-evaluate at the next permitted check. */ }
                catch (Exception error) { schedule.Failures++; if (schedule.Failures == 1) Fault?.Invoke(error); }
                var multiplier = Math.Pow(2, Math.Min(schedule.Failures, 5));
                schedule.Next = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(Math.Max(1000, interval.TotalMilliseconds) * multiplier + Random.Shared.Next(1000, 10000));
            }
            try { await Task.Delay(TimeSpan.FromSeconds(3), token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_worker is not null) await _worker;
        _stop.Dispose();
    }
}
