namespace GitCompass.Terminal.Windows;

/// <summary>Application permission adapter; all creation and input call this gate. A session holds
/// a lifetime lease until its owned process tree and ConPTY have closed.</summary>
public interface ITerminalPermissionGate
{
    void DemandAllowChanges(string projectId);
    ITerminalPermissionLease AcquireTerminalLease(string projectId);
}

public interface ITerminalPermissionLease : IDisposable
{
    CancellationToken Revoked { get; }
}
