using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GitCompass.Desktop;
using GitCompass.Infrastructure.Local;

internal static class StartupCloseChecks
{
    public static void Run(string executable, string root)
    {
        foreach (var missingGit in new[] { false, true })
        {
            var scenario = missingGit ? "missing-git" : "first-run";
            var state = Path.Combine(root, "state-" + scenario); Directory.CreateDirectory(state);
            if (missingGit) WriteMissingGitSettings(state, root);
            var capture = Path.Combine(root, scenario + ".png");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "--smoke", "--capture", capture, "--state-root", state, "--theme", "Light" }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot launch startup regression scenario.");
            if (!process.WaitForExit(30000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("Startup/close timed out: " + scenario); }
            var error = Path.ChangeExtension(capture, ".error.txt");
            if (process.ExitCode != 0 || File.Exists(error)) throw new InvalidOperationException("Startup/close failed: " + scenario + ". " + (File.Exists(error) ? File.ReadAllText(error) : "Exit " + process.ExitCode));
            if (!File.Exists(capture) || !File.ReadAllText(Path.ChangeExtension(capture, ".txt")).Contains("Project: none")) throw new InvalidOperationException("Startup scenario did not capture the isolated no-project screen: " + scenario);
            Console.WriteLine("PASS offscreen " + scenario + ": real executable rendered and closed cleanly. Capture: " + capture);
        }

        var app = new App { IsTestHost = true }; app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Exception? failure = null;
        app.DispatcherUnhandledException += (_, args) => { failure = args.Exception; args.Handled = true; Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); };
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try
            {
                var state = Path.Combine(root, "state-repeated-close"); Directory.CreateDirectory(state); WriteMissingGitSettings(state, root);
                var window = new ShellWindow(state) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
                window.Show(); await window.InitializeAsync(); window.UpdateLayout();
                if (!Descendants<TextBlock>(window).Any(text => text.Text.Contains(ShellWindow.GitMissingMessage))) throw new InvalidOperationException("Missing Git does not display its actionable setup explanation.");
                var canceled = 0; var allowed = 0; var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                window.Closing += (_, args) => { if (args.Cancel) canceled++; else allowed++; };
                window.Closed += (_, _) => closed.SetResult();
                window.Close(); window.Close();
                if (closed.Task.IsCompleted) throw new InvalidOperationException("A repeated close bypassed pending cleanup inside the original Closing event.");
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (canceled != 2 || allowed != 1) throw new InvalidOperationException($"Expected two canceled requests and one deferred close; observed {canceled}/{allowed}.");
                Console.WriteLine("PASS missing-Git native close: setup explanation is visible, repeated requests wait, and exactly one deferred close completes without reentrancy.");
            }
            catch (Exception error) { failure = error; }
            finally { Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        });
        Dispatcher.Run();
        if (failure is not null) throw new InvalidOperationException("Native startup/close regression failed", failure);
        Console.WriteLine("PASS startup/close regressions: 3 scenarios. Isolated evidence: " + root);
    }

    private static void WriteMissingGitSettings(string state, string root) => File.WriteAllText(Path.Combine(state, "settings.json"), JsonSerializer.Serialize(new AppSettings { GitExecutablePath = Path.Combine(root, "unavailable-git.exe") }));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    { if (root is T match) yield return match; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
}
