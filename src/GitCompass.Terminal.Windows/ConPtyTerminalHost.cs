using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GitCompass.Terminal.Windows;

public sealed class ConPtyTerminalHost
{
    public Task<TerminalSession> CreateAsync(string projectId, string projectRoot, string displayName,
        ITerminalPermissionGate permissionGate, int columns = 100, int rows = 30, string? shellPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissionGate);
        permissionGate.DemandAllowChanges(projectId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            throw new PlatformNotSupportedException("The terminal requires Windows ConPTY (build 17763 or later).");
        var root = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        shellPath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        shellPath = Path.GetFullPath(shellPath);
        if (!File.Exists(shellPath)) throw new FileNotFoundException("The selected PowerShell executable is missing.", shellPath);
        var lease = permissionGate.AcquireTerminalLease(projectId);
        try
        {
            permissionGate.DemandAllowChanges(projectId);
            lease.Revoked.ThrowIfCancellationRequested();
            var session = TerminalSession.Start(projectId, root, displayName, shellPath, columns, rows, permissionGate, lease);
            session.AttachRevocation();
            return Task.FromResult(session);
        }
        catch { lease.Dispose(); throw; }
    }
}

public sealed class TerminalOutputEventArgs(string text) : EventArgs
{
    public string Text { get; } = text;
}

public sealed class TerminalSession : IAsyncDisposable
{
    private const int MaximumScrollbackCharacters = 512 * 1024;
    private readonly ITerminalPermissionGate _permission;
    private readonly ITerminalPermissionLease _lease;
    private readonly FileStream _input;
    private readonly FileStream _output;
    private readonly object _lifecycle = new();
    private readonly object _scrollbackLock = new();
    private readonly Queue<string> _scrollback = new();
    private readonly SemaphoreSlim _inputLock = new(1, 1);
    private readonly Task _outputTask;
    private readonly TaskCompletionSource _processExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenRegistration _revocation;
    private IntPtr _pseudoConsole, _process, _job;
    private Task? _closeTask;
    private int _scrollbackLength;
    private bool _truncated;

    private TerminalSession(string projectId, string root, string name, string shell, ITerminalPermissionGate permission,
        ITerminalPermissionLease lease, SafeFileHandle input, SafeFileHandle output, IntPtr pseudoConsole, IntPtr process, IntPtr job)
    {
        ProjectId = projectId; ProjectRoot = root; DisplayName = name; ShellPath = shell;
        _permission = permission; _lease = lease; _pseudoConsole = pseudoConsole; _process = process; _job = job;
        // Anonymous pipes are synchronous. Dedicated worker threads drain output independently
        // of input and ClosePseudoConsole, which can wait for its final output to be read.
        _input = new FileStream(input, FileAccess.Write, 4096, false);
        _output = new FileStream(output, FileAccess.Read, 4096, false);
        _outputTask = Task.Factory.StartNew(ReadOutput, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        _ = Task.Factory.StartNew(ObserveExit, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string ProjectId { get; }
    public string ProjectRoot { get; }
    public string DisplayName { get; }
    public string ShellPath { get; }
    public uint? ExitCode { get; private set; }
    public bool WasForceClosed { get; private set; }
    public Task Completion => _completion.Task;
    public bool IsClosed { get { lock (_lifecycle) return _closeTask is not null; } }
    public event EventHandler<TerminalOutputEventArgs>? OutputReceived;
    public event EventHandler? Activity;

    public string ReadScrollback()
    {
        lock (_scrollbackLock)
            return (_truncated ? "\r\n[Earlier terminal output was truncated.]\r\n" : "") + string.Concat(_scrollback);
    }

    internal void AttachRevocation() => _revocation = _lease.Revoked.Register(() => _ = CloseAsync());

    internal static TerminalSession Start(string projectId, string root, string name, string shell, int columns, int rows,
        ITerminalPermissionGate permission, ITerminalPermissionLease lease)
    {
        ValidateSize(columns, rows);
        SafeFileHandle? inputRead = null, inputWrite = null, outputRead = null, outputWrite = null;
        IntPtr pseudoConsole = IntPtr.Zero, attributes = IntPtr.Zero, environment = IntPtr.Zero, job = IntPtr.Zero;
        Native.ProcessInformation process = default;
        bool success = false, attributesInitialized = false;
        try
        {
            Native.Check(Native.CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0), "Create terminal input pipe");
            Native.Check(Native.CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0), "Create terminal output pipe");
            Native.CheckHResult(Native.CreatePseudoConsole(new Native.Coord((short)columns, (short)rows), inputRead, outputWrite, 0, out pseudoConsole), "Create pseudoconsole");
            UIntPtr attributeSize = UIntPtr.Zero;
            Native.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeSize);
            attributes = Marshal.AllocHGlobal(checked((int)attributeSize.ToUInt64()));
            Native.Check(Native.InitializeProcThreadAttributeList(attributes, 1, 0, ref attributeSize), "Initialize terminal process attributes");
            attributesInitialized = true;
            Native.Check(Native.UpdateProcThreadAttribute(attributes, 0, Native.PseudoConsoleAttribute, pseudoConsole, (UIntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero), "Attach pseudoconsole");
            // Null explicit standard handles let ConPTY supply its console handles, even
            // when this host happens to be launched from an existing parent console.
            var startup = new Native.StartupInfoEx { StartupInfo = new Native.StartupInfo { cb = Marshal.SizeOf<Native.StartupInfoEx>(), dwFlags = 0x00000100 }, lpAttributeList = attributes };
            environment = Marshal.StringToHGlobalUni(BuildEnvironment());
            permission.DemandAllowChanges(projectId);
            lease.Revoked.ThrowIfCancellationRequested();
            var command = new StringBuilder('"' + shell + "\" -NoLogo -NoProfile");
            Native.Check(Native.CreateProcessW(shell, command, IntPtr.Zero, IntPtr.Zero, false,
                Native.ExtendedStartupInfoPresent | Native.CreateUnicodeEnvironment | Native.CreateSuspended,
                environment, root, ref startup, out process), "Start terminal PowerShell");
            // ConPTY attachment consumes these pipe ends while CreateProcess attaches.
            // Keep them alive until the child has been created, as the API requires.
            inputRead.Dispose(); outputWrite.Dispose();
            job = Native.CreateJobObjectW(IntPtr.Zero, null);
            Native.Check(job != IntPtr.Zero, "Create terminal job");
            var limits = new Native.JobExtendedLimitInformation();
            limits.BasicLimitInformation.LimitFlags = Native.JobObjectLimitKillOnJobClose;
            Native.Check(Native.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Native.JobExtendedLimitInformation>()), "Configure terminal job ownership");
            Native.Check(Native.AssignProcessToJobObject(job, process.hProcess), "Assign terminal shell to its job");
            permission.DemandAllowChanges(projectId);
            lease.Revoked.ThrowIfCancellationRequested();
            if (Native.ResumeThread(process.hThread) == uint.MaxValue)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Resume terminal shell");
            var session = new TerminalSession(projectId, root, name, shell, permission, lease, inputWrite, outputRead, pseudoConsole, process.hProcess, job);
            success = true;
            return session;
        }
        finally
        {
            if (attributesInitialized) Native.DeleteProcThreadAttributeList(attributes);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
            if (process.hThread != IntPtr.Zero) Native.CloseHandle(process.hThread);
            inputRead?.Dispose(); outputWrite?.Dispose();
            if (!success)
            {
                if (process.hProcess != IntPtr.Zero) { Native.TerminateProcess(process.hProcess, 1); Native.WaitForSingleObject(process.hProcess, 5000); Native.CloseHandle(process.hProcess); }
                if (job != IntPtr.Zero) Native.CloseHandle(job);
                if (pseudoConsole != IntPtr.Zero)
                {
                    // Startup failure needs independent drainage too: closing ConPTY can
                    // emit a final frame even after its client has been terminated.
                    var drain = Task.Factory.StartNew(() =>
                    {
                        try
                        {
                            using var stream = new FileStream(outputRead!, FileAccess.Read, 8192, false);
                            var buffer = new byte[8192];
                            while (stream.Read(buffer) > 0) { }
                        }
                        catch (IOException) { }
                        catch (ObjectDisposedException) { }
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    Native.ClosePseudoConsole(pseudoConsole);
                    drain.GetAwaiter().GetResult();
                }
                inputWrite?.Dispose(); outputRead?.Dispose();
            }
        }
    }

    // Only non-secret OS variables are copied. GitHub/API tokens, SSH agent values and
    // arbitrary inherited application variables never reach the shell environment.
    internal static string BuildEnvironment()
    {
        string[] allowed = ["SystemRoot", "SystemDrive", "WINDIR", "PATH", "PATHEXT", "TEMP", "TMP", "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "APPDATA", "LOCALAPPDATA", "ProgramFiles", "ProgramFiles(x86)", "ProgramData", "ComSpec", "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE", "OS", "USERNAME", "USERDOMAIN", "PSModulePath"];
        var values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in allowed)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (value is not null && !value.Contains('\0')) values[key] = value;
        }
        values["TERM"] = "xterm-256color";
        values["GITCOMPASS_TERMINAL"] = "1";
        return string.Join('\0', values.Select(pair => $"{pair.Key}={pair.Value}")) + "\0\0";
    }

    public async Task SendInputAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 64 * 1024) throw new ArgumentOutOfRangeException(nameof(text), "Terminal input is limited to 64 KB per request.");
        DemandInputPermission();
        await _inputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DemandInputPermission();
            lock (_lifecycle) if (_closeTask is not null) throw new ObjectDisposedException(nameof(TerminalSession));
            var bytes = Encoding.UTF8.GetBytes(text);
            await Task.Run(() => { _input.Write(bytes); _input.Flush(); }, cancellationToken).ConfigureAwait(false);
            RaiseActivity();
        }
        finally { _inputLock.Release(); }
    }

    public Task InterruptAsync(CancellationToken cancellationToken = default) => SendInputAsync("\x03", cancellationToken);

    internal void DemandInputPermission()
    {
        _permission.DemandAllowChanges(ProjectId);
        _lease.Revoked.ThrowIfCancellationRequested();
        lock (_lifecycle) if (_closeTask is not null) throw new ObjectDisposedException(nameof(TerminalSession));
    }

    public void Resize(int columns, int rows)
    {
        ValidateSize(columns, rows);
        lock (_lifecycle)
        {
            if (_closeTask is not null) return;
            Native.CheckHResult(Native.ResizePseudoConsole(_pseudoConsole, new Native.Coord((short)columns, (short)rows)), "Resize pseudoconsole");
        }
    }

    private static void ValidateSize(int columns, int rows)
    {
        if (columns is < 2 or > 500 || rows is < 1 or > 300) throw new ArgumentOutOfRangeException(nameof(columns), "Terminal size is outside supported bounds.");
    }

    private void ReadOutput()
    {
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[8192]; var chars = new char[8192];
        try
        {
            int count;
            while ((count = _output.Read(bytes)) > 0)
            {
                var charCount = decoder.GetChars(bytes, 0, count, chars, 0, false);
                Publish(new string(chars, 0, charCount));
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private void Publish(string text)
    {
        lock (_scrollbackLock)
        {
            _scrollback.Enqueue(text); _scrollbackLength += text.Length;
            while (_scrollbackLength > MaximumScrollbackCharacters && _scrollback.TryDequeue(out var first))
            { _scrollbackLength -= first.Length; _truncated = true; }
        }
        try { OutputReceived?.Invoke(this, new TerminalOutputEventArgs(text)); } catch { /* UI subscribers must not stop pipe draining. */ }
    }

    private void RaiseActivity() { try { Activity?.Invoke(this, EventArgs.Empty); } catch { } }

    private void ObserveExit()
    {
        var handle = _process;
        Native.WaitForSingleObject(handle, uint.MaxValue);
        if (Native.GetExitCodeProcess(handle, out var exitCode)) ExitCode = exitCode;
        _processExited.TrySetResult();
        RaiseActivity();
        _ = CloseAsync();
    }

    public Task CloseAsync()
    {
        lock (_lifecycle) return _closeTask ??= Task.Run(CloseOwnedProcessesAsync);
    }

    private async Task CloseOwnedProcessesAsync()
    {
        try
        {
            // Close the owned process tree first. Closing the ConPTY while its pipe isn't
            // being drained can deadlock; the dedicated output worker remains alive here.
            if (_process != IntPtr.Zero && Native.GetExitCodeProcess(_process, out var state)) WasForceClosed = state == 259;
            if (_job != IntPtr.Zero) Native.TerminateJobObject(_job, 1);
            if (_process != IntPtr.Zero) Native.WaitForSingleObject(_process, 10000);
            await _processExited.Task.ConfigureAwait(false);
            _input.Dispose();
            if (_pseudoConsole != IntPtr.Zero) { Native.ClosePseudoConsole(_pseudoConsole); _pseudoConsole = IntPtr.Zero; }
            await _outputTask.ConfigureAwait(false);
            _output.Dispose();
            if (WasForceClosed) Publish("\r\n[Session closed. Its owned shell and child processes were stopped. Recheck project state before continuing an interrupted operation.]\r\n");
        }
        finally
        {
            if (_process != IntPtr.Zero) { Native.CloseHandle(_process); _process = IntPtr.Zero; }
            if (_job != IntPtr.Zero) { Native.CloseHandle(_job); _job = IntPtr.Zero; }
            _revocation.Unregister();
            _lease.Dispose();
            _completion.TrySetResult();
            RaiseActivity();
        }
    }

    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);
}
