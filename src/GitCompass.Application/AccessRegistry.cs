using GitCompass.Domain;

namespace GitCompass.Application;

/// <summary>Session-scoped permissions. Revocation is immediate for new/queued work; active leases finish or cancel before downgrade completes.</summary>
public sealed class AccessRegistry
{
    private sealed class Entry { public AccessLevel Level; public AccessLevel? Pending; public long Generation = 1; public string Group = ""; public readonly HashSet<AccessLease> Active = []; }
    private readonly object sync = new();
    private readonly Dictionary<Guid, Entry> entries = [];
    public Guid SessionId { get; } = Guid.NewGuid();
    public event Action<AccessGrant>? Changed;
    public void Register(Guid projectId, bool rememberedMonitor = false, string? repositoryIdentity = null)
    {
        lock (sync)
        {
            if (entries.TryGetValue(projectId, out var existing)) { if (repositoryIdentity != null) existing.Group = repositoryIdentity; }
            else entries.Add(projectId, new Entry { Level = rememberedMonitor ? AccessLevel.MonitorOnly : AccessLevel.ReadOnly, Group = repositoryIdentity ?? projectId.ToString() });
        }
    }
    public AccessGrant Get(Guid projectId) { lock (sync) { var e = Find(projectId); return Grant(projectId, e); } }
    public bool HasActive(Guid projectId, OperationKind kind) { lock (sync) return Find(projectId).Active.Any(l => l.Kind == kind); }
    public AccessGrant SetLevel(Guid projectId, AccessLevel level)
    {
        AccessGrant grant; AccessLease[] cancel = [];
        lock (sync)
        {
            var e = Find(projectId); e.Generation++;
            if (level < e.Level && e.Active.Count > 0) { e.Pending = level; cancel = e.Active.ToArray(); }
            else { e.Level = level; e.Pending = null; }
            grant = Grant(projectId, e);
        }
        foreach (var lease in cancel) lease.RequestCancellation();
        Changed?.Invoke(grant); return grant;
    }
    public void Authorize(Guid projectId, OperationKind kind, long? expectedGeneration = null)
    {
        lock (sync)
        {
            var e = Find(projectId);
            if (expectedGeneration.HasValue && e.Generation != expectedGeneration) throw new AccessDeniedException("Access changed after review. Create a new preview.");
            var effective = e.Pending ?? e.Level;
            var required = Required(kind);
            if (effective < required || e.Pending.HasValue) throw new AccessDeniedException($"{kind} requires {required}. Current access is {effective}" + (e.Pending.HasValue ? " (transition pending)." : "."));
            var groupActive = entries.Values.Where(other => string.Equals(other.Group, e.Group, StringComparison.OrdinalIgnoreCase)).SelectMany(other => other.Active);
            if (kind == OperationKind.Terminal && groupActive.Any(l => l.Kind is not (OperationKind.Terminal or OperationKind.GitHubRead or OperationKind.Inspect))) throw new AccessDeniedException("A managed operation owns this repository's shared Git metadata. Wait for its result before terminal input.");
            if (kind is not (OperationKind.Terminal or OperationKind.GitHubRead or OperationKind.Inspect) && groupActive.Any(l => l.Kind == OperationKind.Terminal)) throw new AccessDeniedException("Close active terminal sessions sharing this repository's Git metadata before a managed operation.");
        }
    }
    public AccessLease Acquire(Guid projectId, OperationKind kind, long? expectedGeneration = null)
    {
        lock (sync)
        {
            Authorize(projectId, kind, expectedGeneration);
            var e = Find(projectId); var lease = new AccessLease(this, projectId, kind, Grant(projectId, e)); e.Active.Add(lease); return lease;
        }
    }
    public static AccessLevel Required(OperationKind kind) => kind == OperationKind.Inspect ? AccessLevel.ReadOnly : kind is OperationKind.Fetch or OperationKind.GitHubRead ? AccessLevel.MonitorOnly : AccessLevel.AllowChanges;
    private Entry Find(Guid id) => entries.TryGetValue(id, out var e) ? e : throw new AccessDeniedException("This project is not registered in the current session.");
    private AccessGrant Grant(Guid id, Entry e) => new(id, e.Level, SessionId, e.Generation, e.Pending);
    internal void Release(AccessLease lease)
    {
        AccessGrant? changed = null;
        lock (sync) { var e = Find(lease.ProjectId); e.Active.Remove(lease); if (e.Active.Count == 0 && e.Pending.HasValue) { e.Level = e.Pending.Value; e.Pending = null; changed = Grant(lease.ProjectId, e); } }
        if (changed != null) Changed?.Invoke(changed);
    }
}
public sealed class AccessLease : IDisposable
{
    private AccessRegistry? owner;
    private readonly CancellationTokenSource cancellation = new();
    internal AccessLease(AccessRegistry owner, Guid projectId, OperationKind kind, AccessGrant grant) { this.owner = owner; ProjectId = projectId; Kind = kind; Grant = grant; }
    public Guid ProjectId { get; }
    public OperationKind Kind { get; }
    public AccessGrant Grant { get; }
    public CancellationToken Revoked => cancellation.Token;
    internal void RequestCancellation() { try { cancellation.Cancel(); } catch (ObjectDisposedException) { } }
    public void Dispose() { var registry = Interlocked.Exchange(ref owner, null); if (registry != null) { registry.Release(this); cancellation.Dispose(); } }
}
