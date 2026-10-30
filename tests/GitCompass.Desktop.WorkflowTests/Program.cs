using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GitCompass.Application;
using GitCompass.Desktop;
using GitCompass.Domain;

internal static class Program
{
    private static int _passed, _exitCode;
    private static ShellWindow? _window;
    private static readonly string Fixture = Path.Combine(Path.GetFullPath(".artifacts"), "desktop-workflow", Guid.NewGuid().ToString("N"));

    [STAThread]
    public static int Main(string[] args)
    {
        var app = new App { IsTestHost = true }; app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.DispatcherUnhandledException += (_, args) => { Console.Error.WriteLine(args.Exception); _exitCode = 1; args.Handled = true; Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); };
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try
            {
                if (args.Contains("--appearance-only"))
                {
                    Directory.CreateDirectory(Fixture);
                    _window = new ShellWindow(Path.Combine(Fixture, "state")) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
                    _window.Show(); await _window.InitializeAsync();
                    await BeginnerWorkflowChecks.RunAppearanceOnlyAsync(_window, Check);
                }
                else await ExerciseAsync();
            }
            catch (Exception error) { _exitCode = 1; Console.Error.WriteLine(error); }
            finally { _window?.Close(); Console.WriteLine($"Desktop workflow: {_passed} checks passed. Fixture: {Fixture}"); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        });
        Dispatcher.Run();
        return _exitCode;
    }

    private static async Task ExerciseAsync()
    {
        Directory.CreateDirectory(Fixture);
        var repository = Path.Combine(Fixture, "project"); Directory.CreateDirectory(repository);
        await Git(repository, "init", "--initial-branch=main");
        await Git(repository, "config", "user.name", "GitCompass fixture"); await Git(repository, "config", "user.email", "fixture@example.invalid");
        await Git(repository, "config", "core.autocrlf", "false");
        await File.WriteAllTextAsync(Path.Combine(repository, "readme.txt"), "Recorded first version\n");
        await Git(repository, "add", "--", "readme.txt"); await Git(repository, "commit", "-m", "Fixture initial version");
        var initialHead = (await Git(repository, "rev-parse", "HEAD")).Trim();
        await File.AppendAllTextAsync(Path.Combine(repository, "readme.txt"), "Reviewed UI workflow change\n");
        var beforeIndex = await File.ReadAllBytesAsync(Path.Combine(repository, ".git", "index"));
        _window = new ShellWindow(Path.Combine(Fixture, "state")) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false }; _window.Show();
        await _window.InitializeAsync(); await _window.OpenProjectAsync(repository);
        Check(System.Windows.Application.Current.Windows.Count == 1 && System.Windows.Application.Current.Windows.Cast<Window>().All(window => window == _window), "test host opens only its isolated fixture window and suppresses production startup");
        _window.ConfigureView("Changes", "Light"); _window.UpdateLayout();
        var workspace = (GitWorkspace)typeof(ShellWindow).GetField("workspace", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_window)!;
        var project = workspace.Projects.Single();
        Check(workspace.Access.Get(project.Id).Level == AccessLevel.ReadOnly, "native shell opens actual repository in Read-only");
        var afterIndex = await File.ReadAllBytesAsync(Path.Combine(repository, ".git", "index"));
        Check(beforeIndex.SequenceEqual(afterIndex), "opening/rendering Changes preserves repository index bytes");
        Check(!FindButton("Review staging selected files").IsEnabled, "stage control explains and enforces disabled Read-only state");

        // This fixture was created by this test. Trust setup is injected only into the test's private workspace;
        // actual access change, selection, review and execution are driven through rendered native controls.
        workspace.SetTrusted(project.Id, true);
        await _window.OpenProjectAsync(repository); _window.ConfigureView("Changes", "Light"); _window.UpdateLayout();
        ((ComboBox)_window.FindName("AccessSelector")).SelectedIndex = (int)AccessLevel.AllowChanges;
        await Until(() => workspace.Access.Get(project.Id).Level == AccessLevel.AllowChanges && FindButton("Review staging selected files").IsEnabled);
        Check(workspace.Access.Get(project.Id).Level == AccessLevel.AllowChanges, "Access selector unlocks the correct project session");
        var files = Descendants<ListBox>(_window).First(list => AutomationProperties.GetName(list) == "Unstaged and new files"); files.SelectedIndex = 0;
        Click("Review staging selected files"); await Until(() => HasButton("Run reviewed operation"));
        Check(Descendants<TextBox>(_window).Any(box => box.Text.Contains("git --literal-pathspecs add")), "review displays actual underlying stage command");
        Click("Run reviewed operation");
        await Until(() => workspace.GetSnapshot(project.Id)?.Files.Any(file => file.IsStaged) == true && HasButton("Review local commit"));
        Check((await Git(repository, "diff", "--cached", "--", "readme.txt")).Contains("Reviewed UI workflow change"), "native stage review executes only selected real file contents");
        var message = Descendants<TextBox>(_window).First(box => AutomationProperties.GetName(box).StartsWith("Commit message")); message.Text = "Save reviewed native workflow version";
        Click("Review local commit"); await Until(() => HasButton("Run reviewed operation")); Click("Run reviewed operation");
        await Until(() => workspace.GetSnapshot(project.Id)?.HeadId != initialHead && HasButton("Review local commit"));
        var newHead = (await Git(repository, "rev-parse", "HEAD")).Trim();
        Check(newHead != initialHead && (await Git(repository, "show", "HEAD:readme.txt")).Contains("Reviewed UI workflow change"), "native commit review records expected contents locally");
        Check((await Git(repository, "remote")).Trim().Length == 0, "local commit workflow creates no remote or upload");
        _window.ConfigureView("History graph", "Dark"); _window.UpdateLayout();
        Check(_window.SmokeDescription.Contains("History rows: 2"), "History page reflects two real recorded commits after operation refresh");
        ((ComboBox)_window.FindName("AccessSelector")).SelectedIndex = (int)AccessLevel.ReadOnly;
        await Until(() => workspace.Access.Get(project.Id).Level == AccessLevel.ReadOnly);
        Check(workspace.Access.Get(project.Id).Level == AccessLevel.ReadOnly, "native selector locks back after local workflow");
        await BeginnerWorkflowChecks.RunOnboardingStateAsync(Path.Combine(Fixture, "onboarding-state"), Check);
        await BeginnerWorkflowChecks.RunAsync(_window, Path.Combine(Path.GetFullPath(".artifacts"), "native-beginner", Guid.NewGuid().ToString("N")), Check);
    }

    private static async Task Until(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(45))
        { _window!.UpdateLayout(); try { if (condition()) return; } catch (InvalidOperationException) { /* Controls are rebuilt after observation and review. */ } await Task.Delay(30); }
        throw new TimeoutException("Native workflow condition did not complete. " + _window!.SmokeDescription);
    }
    private static void Check(bool result, string description)
    { if (!result) throw new InvalidOperationException(description); _passed++; Console.WriteLine("PASS " + description); }
    private static bool HasButton(string label) => Descendants<Button>(_window!).Any(button => button.Content?.ToString() == label);
    private static Button FindButton(string label) => Descendants<Button>(_window!).First(button => button.Content?.ToString() == label);
    private static void Click(string label) => FindButton(label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static async Task<string> Git(string repository, params string[] args)
    {
        var path = GitCompass.Infrastructure.Local.DependencyProbe.FindGitExecutable() ?? throw new FileNotFoundException("Git required for test fixture.");
        var info = new ProcessStartInfo(path) { WorkingDirectory = repository, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("--no-pager"); foreach (var arg in args) info.ArgumentList.Add(arg);
        foreach (var key in info.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(key);
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); await Task.WhenAll(output, error);
        if (process.ExitCode != 0) throw new InvalidOperationException(await error);
        return await output;
    }
}
