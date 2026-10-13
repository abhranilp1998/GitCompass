using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using GitCompass.Application;
using GitCompass.Domain;
using GitCompass.Terminal.Windows;
using GitCompass.Infrastructure.Local;
using Microsoft.Web.WebView2.Wpf;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--webview")) return WebViewSmoke();
        try { if (args.Contains("--inventory")) InventoryAsync().GetAwaiter().GetResult(); else RunAsync().GetAwaiter().GetResult(); return 0; }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static async Task InventoryAsync()
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Environment.CurrentDirectory, "assets", "learning", "manifest.json")));
        var declared = manifest.RootElement.GetProperty("SupportedCoreCommands").EnumerateArray().Select(item => item.GetString()!).ToArray();
        var entries = manifest.RootElement.GetProperty("Entries").EnumerateArray().ToArray();
        var ids = entries.Select(entry => entry.GetProperty("Id").GetString()!).ToArray();
        Check(declared.Distinct(StringComparer.Ordinal).Count() == declared.Length && ids.Distinct(StringComparer.Ordinal).Count() == ids.Length, "Learning declared commands and entries are unique");
        Check(declared.All(command => ids.Count(id => id == "git-" + command) == 1), "Every declared core command has one basic reference entry");
        Check(entries.All(entry => new[] { "Purpose", "Prerequisites", "Effects", "WhenToPause", "Example", "Outcome", "Practice" }.All(field => !string.IsNullOrWhiteSpace(entry.GetProperty(field).GetString()))), "Every lesson includes purpose, prerequisites, effects, pause advice, example, outcome and practice");
        var git = DependencyProbe.FindGitExecutable() ?? throw new FileNotFoundException("Installed Git not detected");
        var result = await GitCommandInventory.ReadAsync(git, declared);
        Check(result.Error is null && result.Commands.Length > 100, "Real installed Git command inventory enumerates offline with bounded typed query");
        Check(result.Commands.All(command => !command.Section.Contains("interfaces", StringComparison.OrdinalIgnoreCase)) && result.DocumentationInterfaces.Length > 0, "Documentation interfaces remain separate from executable command inventory");
        Check(result.UnsupportedInstalledCommands.All(name => !declared.Contains(name)), "Installed commands beyond the declared catalog are explicitly unsupported");
        var parsed = GitCommandInventory.Parse("fixture", "Main Porcelain Commands\n   status                  Show status\n   new-command             New unsupported command\n\nUser-facing repository, command and file interfaces\n   attributes              Documentation only\n", ["status", "push"]);
        Check(parsed.Commands.Length == 2 && parsed.UnsupportedInstalledCommands.SequenceEqual(["new-command"]) && parsed.DeclaredCommandsMissingFromInventory.SequenceEqual(["push"]) && parsed.DocumentationInterfaces[0].Name == "attributes", "Inventory reconciliation preserves command sections and distinguishes unsupported/missing/documentation entries");
        Console.WriteLine($"Installed version: {result.Version}; declared: {declared.Length}; lessons: {entries.Length}; installed commands: {result.Commands.Length}; supported installed: {result.SupportedInstalledCount}; unsupported installed: {result.UnsupportedInstalledCommands.Length}; declared absent: {result.DeclaredCommandsMissingFromInventory.Length}; documentation interfaces: {result.DocumentationInterfaces.Length}");
        Console.WriteLine("Unsupported installed: " + string.Join(", ", result.UnsupportedInstalledCommands));
        Console.WriteLine("Declared absent from help inventory: " + string.Join(", ", result.DeclaredCommandsMissingFromInventory));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }

    private static async Task UntilAsync(Func<bool> condition, string message, int timeoutSeconds = 20)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(timeoutSeconds)) throw new TimeoutException(message);
            await Task.Delay(50);
        }
    }

    private static string Plain(TerminalSession session) => Regex.Replace(session.ReadScrollback(), "\x1b\\[[0-?]*[ -/]*[@-~]", "");

    private static async Task RunAsync()
    {
        Console.WriteLine($"OS: {Environment.OSVersion}; WebView2: {TerminalView.DetectRuntimeVersion()}");
        var temp = Path.Combine(Path.GetTempPath(), "GitCompass-terminal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var access = new AccessRegistry(); var id1 = Guid.NewGuid(); var id2 = Guid.NewGuid();
        access.Register(id1); access.Register(id2);
        var gate = new AccessRegistryTerminalGate(access); var host = new ConPtyTerminalHost();
        try
        {
            try { await host.CreateAsync(id1.ToString(), temp, "Denied", gate); throw new Exception("Read-only terminal unexpectedly started"); }
            catch (AccessDeniedException) { Console.WriteLine("PASS Read-only denies shell creation before process start"); }
            access.SetLevel(id1, AccessLevel.MonitorOnly);
            try { await host.CreateAsync(id1.ToString(), temp, "Denied", gate); throw new Exception("Monitor terminal unexpectedly started"); }
            catch (AccessDeniedException) { Console.WriteLine("PASS Monitor only denies shell creation"); }
            access.SetLevel(id1, AccessLevel.AllowChanges); access.SetLevel(id2, AccessLevel.AllowChanges);
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", "gitcompass-test-secret-must-not-inherit");
            await using var one = await host.CreateAsync(id1.ToString(), temp, "One", gate);
            await using var two = await host.CreateAsync(id2.ToString(), temp, "Two", gate);
            await UntilAsync(() => Plain(one).Contains("PS ") && Plain(two).Contains("PS "), "PowerShell prompts");
            await one.SendInputAsync("[Console]::WriteLine('SESSION_'+[string](1)); [Console]::WriteLine('TOKEN_ABSENT_'+[string]([string]::IsNullOrEmpty($env:GITHUB_TOKEN)))\r");
            await two.SendInputAsync("[Console]::WriteLine('SESSION_'+[string](2))\r");
            await UntilAsync(() => Plain(one).Contains("SESSION_1") && Plain(two).Contains("SESSION_2"), "Two real session command outputs");
            Check(!Plain(one).Contains("SESSION_2") && !Plain(two).Contains("SESSION_1"), "Session outputs and project ownership are isolated");
            await UntilAsync(() => Plain(one).Contains("TOKEN_ABSENT_True"), "Token isolation");
            Check(!Plain(one).Contains("gitcompass-test-secret-must-not-inherit"), "API token is absent from shell environment");
            one.Resize(120, 40); two.Resize(80, 25);
            await one.SendInputAsync("[Console]::WriteLine('SIZE_'+[string][Console]::WindowWidth+'x'+[string][Console]::WindowHeight)\r");
            await UntilAsync(() => Plain(one).Contains("SIZE_120x40"), "ConPTY resize observed by real shell");
            Check(true, "Real ConPTY resize changes PowerShell console dimensions");
            await one.SendInputAsync("[Console]::Write(('OUTPUT_BOUNDED_'+('a'*600000)))\r");
            await UntilAsync(() => one.ReadScrollback().Contains("truncated"), "Bounded scrollback under output pressure");
            Check(one.ReadScrollback().Length < 530000, "Large output is bounded and truncation is visible");
            await two.SendInputAsync("[Console]::WriteLine('WAIT_'+[string](1)); Start-Sleep -Seconds 60\r");
            await UntilAsync(() => Plain(two).Contains("WAIT_1"), "Long-running command started");
            await two.InterruptAsync(); await Task.Delay(400);
            await two.SendInputAsync("[Console]::WriteLine('INTERRUPTED_'+[string](1))\r");
            await UntilAsync(() => Plain(two).Contains("INTERRUPTED_1"), "Ctrl+C interrupted sleep", 10);
            Check(true, "Ctrl+C interrupts running shell work and allows further input");
            await one.SendInputAsync("$child = Start-Process -FilePath $env:ComSpec -ArgumentList '/c ping -n 120 127.0.0.1 > nul' -PassThru -WindowStyle Hidden; [Console]::WriteLine('CHILD_'+[string]$child.Id)\r");
            await UntilAsync(() => Regex.IsMatch(Plain(one), "CHILD_[0-9]+"), "Owned child process started");
            var childPid = int.Parse(Regex.Match(Plain(one), "CHILD_([0-9]+)").Groups[1].Value);
            var pending = access.SetLevel(id1, AccessLevel.ReadOnly);
            Check(pending.PendingLevel == AccessLevel.ReadOnly, "Downgrade stays pending until shell/job closes");
            try { await one.SendInputAsync("Write-Output denied\r"); throw new Exception("Revoked input succeeded"); }
            catch (AccessDeniedException) { Console.WriteLine("PASS Input denied immediately on revocation"); }
            await one.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            Check(access.Get(id1).Level == AccessLevel.ReadOnly && access.Get(id1).PendingLevel is null, "Access becomes Read-only only after terminal lease releases");
            bool childAlive;
            try { using var child = Process.GetProcessById(childPid); childAlive = !child.HasExited; } catch (ArgumentException) { childAlive = false; }
            Check(!childAlive, "Session shutdown kills owned descendant process");
            await two.SendInputAsync("exit\r");
            await two.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            Check(one.IsClosed && two.IsClosed, "Both ConPTY sessions shut down cleanly");
            access.SetLevel(id1, AccessLevel.AllowChanges);
            for (var rapid = 0; rapid < 5; rapid++)
            {
                await using var immediate = await host.CreateAsync(id1.ToString(), temp, "Immediate close", gate);
                await immediate.CloseAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            Check(!access.HasActive(id1, OperationKind.Terminal), "Immediate startup/shutdown releases every lease without a lifecycle race");
            var invalidShell = Path.Combine(temp, "invalid-shell.exe"); await File.WriteAllTextAsync(invalidShell, "Not an executable");
            try
            {
                await host.CreateAsync(id1.ToString(), temp, "Invalid shell", gate, shellPath: invalidShell).WaitAsync(TimeSpan.FromSeconds(10));
                throw new Exception("Invalid shell unexpectedly started");
            }
            catch (System.ComponentModel.Win32Exception) { Console.WriteLine("PASS Failed process startup closes ConPTY/pipes and releases permission lease"); }
            Check(!access.HasActive(id1, OperationKind.Terminal), "Failed terminal startup leaves no active application lease");
            access.Register(id1, repositoryIdentity: temp); access.Register(id2, repositoryIdentity: temp.ToUpperInvariant());
            using (var managed = access.Acquire(id1, OperationKind.Stage))
            {
                try { await host.CreateAsync(id2.ToString(), temp, "Shared metadata denied", gate); throw new Exception("Linked-project terminal unexpectedly bypassed a managed lease"); }
                catch (AccessDeniedException) { Console.WriteLine("PASS Shared metadata managed lease denies a second project's shell creation atomically"); }
            }
            await using (var shared = await host.CreateAsync(id2.ToString(), temp, "Shared metadata terminal", gate))
            {
                try { using var denied = access.Acquire(id1, OperationKind.Stage); throw new Exception("Managed lease unexpectedly bypassed shared terminal ownership"); }
                catch (AccessDeniedException) { Console.WriteLine("PASS Shared terminal lease denies another project's managed operation atomically"); }
            }
            Check(!access.HasActive(id2, OperationKind.Terminal), "Shared metadata terminal closure releases its owned group lease");
            Check(!TerminalBridge.TryParse("https://evil.invalid/", "{\"type\":\"input\",\"data\":\"x\"}", out _), "Bridge rejects foreign origins");
            foreach (var json in new[] { "{}", "{\"type\":\"execute\",\"command\":\"x\"}", "{\"type\":\"resize\",\"columns\":-1,\"rows\":10}", "{\"type\":\"input\",\"data\":\"x\",\"path\":\"C:\\\\secret\"}", "{\"type\":\"ready\",\"type\":\"ready\"}" })
                Check(!TerminalBridge.TryParse(TerminalBridge.Document, json, out _), "Bridge rejects malformed/unrecognized schema");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
            // Dedicated test root only. No repository or user project contents are touched.
            Directory.Delete(temp, true);
        }
    }

    private static int WebViewSmoke()
    {
        var result = 1; var app = new System.Windows.Application();
        var window = new Window { Title = "GitCompass terminal renderer smoke", Width = 900, Height = 400, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Opacity = 0 };
        window.Loaded += async (_, _) =>
        {
            TerminalView? view = null;
            try
            {
                var access = new AccessRegistry(); var id = Guid.NewGuid(); access.Register(id); access.SetLevel(id, AccessLevel.AllowChanges);
                var session = await new ConPtyTerminalHost().CreateAsync(id.ToString(), Path.GetTempPath(), "Renderer proof", new AccessRegistryTerminalGate(access));
                view = new TerminalView(session); window.Content = view;
                await view.InitializeAsync();
                await UntilAsync(() => view.RendererReady, "Local xterm.js bridge ready");
                await session.SendInputAsync("[Console]::WriteLine('RENDERER_'+[string](1))\r");
                await UntilAsync(() => Plain(session).Contains("RENDERER_1"), "Renderer session real output");
                await view.PasteAsync("[Console]::WriteLine('PASTED_'+[string](1))");
                await session.SendInputAsync("\r");
                await UntilAsync(() => Plain(session).Contains("PASTED_1"), "Native single-line paste reached real terminal");
                Check(true, "Native paste uses session input and real PowerShell executes after Enter");
                var rendered = "";
                for (var i = 0; i < 50 && !rendered.Contains("RENDERER_1"); i++) { await Task.Delay(50); rendered = await view.ReadRenderedScreenAsync(); }
                Check(rendered.Contains("RENDERER_1"), "WebView2 offline xterm.js buffer contains real ConPTY output");
                var core = ((WebView2)view.Content).CoreWebView2;
                await session.SendInputAsync("[Console]::Write(([char]27)+']52;c;aW52YWxpZA=='+([char]7)); [Console]::Write(([char]27)+']8;;https://evil.invalid'+([char]7)+'fake link'+([char]27)+']8;;'+([char]7)); [Console]::WriteLine('<script>window.gitCompassPwned=true</script>')\r");
                await UntilAsync(() => Plain(session).Contains("fake link"), "Untrusted output reached ConPTY");
                await Task.Delay(250);
                Check(await core.ExecuteScriptAsync("typeof window.gitCompassPwned") == "\"undefined\"", "Terminal output cannot execute HTML or script");
                Check(await core.ExecuteScriptAsync("document.querySelectorAll('a').length") == "0", "Terminal fake links create no executable browser links");
                var origin = core.Source;
                core.Navigate("https://evil.invalid/"); await Task.Delay(250);
                Check(core.Source == origin, "WebView2 navigation blocks external origins");
                result = 0;
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); }
            finally { if (view is not null) await view.DisposeAsync(); window.Close(); }
        };
        app.Run(window); return result;
    }
}
