using System.Diagnostics;
using System.Text;

namespace GitCompass.Infrastructure.Git;

internal sealed record ProcessResult(int ExitCode, string Output, string Error, bool Truncated, bool Canceled);

internal sealed class GitProcess
{
    private readonly string executable;
    internal GitProcess(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)) throw new FileNotFoundException("Git executable must be an existing absolute path.", executable);
        this.executable = Path.GetFullPath(executable);
    }
    internal async Task<ProcessResult> RunAsync(string cwd, IEnumerable<string> arguments, CancellationToken cancellation, string? input = null, bool inspection = true, Action<string, string>? output = null, int limit = 4 * 1024 * 1024)
    {
        cancellation.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(executable) { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, StandardOutputEncoding = new UTF8Encoding(false, true), StandardErrorEncoding = new UTF8Encoding(false, false), StandardInputEncoding = new UTF8Encoding(false) };
        foreach (var arg in new[] { "--no-pager", "-c", "core.fsmonitor=false", "-c", "core.untrackedCache=false", "-c", "submodule.recurse=false", "-c", "maintenance.auto=false", "-c", "gc.auto=0" }.Concat(arguments)) info.ArgumentList.Add(arg);
        // Do not let an inherited shell environment retarget the selected repository or inject configuration.
        foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(key);
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        // Inspection must not silently fetch missing partial-clone objects or invoke a transport.
        if (inspection) info.Environment["GIT_NO_LAZY_FETCH"] = "1";
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GCM_INTERACTIVE"] = "Never";
        info.Environment["GIT_PAGER"] = "cat";
        info.Environment["GIT_EDITOR"] = "true";
        info.Environment["LC_ALL"] = "C";
        using var job = OperatingSystem.IsWindows() ? new OwnedProcessJob() : null;
        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.Start();
        try { job?.Assign(process.SafeHandle.DangerousGetHandle()); }
        catch (Exception assignmentError)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return new(-1, "", "Git process ownership could not be established after launch: " + assignmentError.Message + ". Its partial effects are unknown; inspect before retrying.", false, true);
        }
        var canceled = false;
        using var registration = cancellation.Register(() => { canceled = true; job?.Terminate(); try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        var stdout = Capture(process.StandardOutput, "stdout"); var stderr = Capture(process.StandardError, "stderr");
        try { if (input != null) await process.StandardInput.WriteAsync(input); process.StandardInput.Close(); } catch (IOException) { }
        await process.WaitForExitAsync(CancellationToken.None);
        // A hook may leave a child holding inherited stdout/stderr after root Git exits. Kill only our owned descendants before waiting for EOF.
        var combined = Task.WhenAll(stdout, stderr);
        // Git for Windows' launch wrapper can exit just before its child's final pipe close. Give completed output a brief drain grace.
        await Task.WhenAny(combined, Task.Delay(TimeSpan.FromMilliseconds(250)));
        var orphaned = !combined.IsCompleted && job?.HasActiveProcesses == true;
        if (orphaned) { job!.Terminate(); canceled = true; }
        job?.Dispose();
        var timedOut = await Task.WhenAny(combined, Task.Delay(TimeSpan.FromSeconds(3))) != combined;
        if (timedOut) { canceled = true; process.StandardOutput.Dispose(); process.StandardError.Dispose(); }
        var captured = await combined;
        return new(process.ExitCode, captured[0].Text, captured[1].Text + (orphaned ? "\nOwned helper processes remained after Git exited and were stopped. Inspect the operation's outcome." : timedOut ? "\nOutput did not finish after Git exited; pipes were closed. Output is incomplete." : ""), timedOut || orphaned || captured.Any(c => c.Truncated), canceled);

        async Task<(string Text, bool Truncated)> Capture(StreamReader reader, string stream)
        {
            var text = new StringBuilder(); var buffer = new char[4096]; var truncated = false; int count;
            try
            {
                while ((count = await reader.ReadAsync(buffer)) > 0)
                {
                    var chunk = new string(buffer, 0, count);
                    // A logging/UI subscriber must never stop either pipe being drained.
                    try { output?.Invoke(stream, chunk); } catch (Exception) { }
                    var room = Math.Max(0, limit - text.Length); if (count > room) truncated = true;
                    if (room > 0) text.Append(chunk.AsSpan(0, Math.Min(room, count)));
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { truncated = true; }
            return (text.ToString(), truncated);
        }
    }
}
