using GitCompass.Application;
using GitCompass.Domain;

namespace GitCompass.Terminal.Windows;

/// <summary>The terminal's application-level gate. Read-only/Monitor only can never start a shell.</summary>
public sealed class AccessRegistryTerminalGate(AccessRegistry access) : ITerminalPermissionGate
{
    public void DemandAllowChanges(string projectId) => access.Authorize(Guid.Parse(projectId), OperationKind.Terminal);
    public ITerminalPermissionLease AcquireTerminalLease(string projectId) => new Lease(access.Acquire(Guid.Parse(projectId), OperationKind.Terminal));
    private sealed class Lease(AccessLease lease) : ITerminalPermissionLease
    {
        public CancellationToken Revoked => lease.Revoked;
        public void Dispose() => lease.Dispose();
    }
}
