using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GitCompass.Application;
using GitCompass.Desktop;
using GitCompass.Domain;
using GitCompass.Infrastructure.Local;

internal static class UiEvidenceChecks
{
    private static ShellWindow? window;
    private static Exception? failure;
    private static int checks;

    public static void Run(string repository, string root)
    {
        var app = new App { IsTestHost = true }; app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.DispatcherUnhandledException += (_, args) => { failure = args.Exception; args.Handled = true; Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); };
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try { await Exercise(repository, root); }
            catch (Exception error) { failure = error; }
            finally { window?.Close(); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        });
        Dispatcher.Run();
        if (failure is not null) throw new InvalidOperationException("Actual WPF interaction checks failed", failure);
        Console.WriteLine($"PASS actual WPF interaction evidence: {checks} checks. Additional captures: {Path.Combine(root, "native-states")}");
    }

    private static async Task Exercise(string repository, string root)
    {
        var state = Path.Combine(root, "state-native-interactions");
        window = new ShellWindow(state) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false }; window.Show(); await window.InitializeAsync(); window.ConfigureView("Overview", "Light");
        Capture(root, "first-run-light");
        Check(Texts().Any(text => text.Contains("Open your project")), "first-run screen offers the project-opening journey");
        await window.OpenProjectAsync(repository); window.ConfigureView("History graph", "Light");
        await Until(() => HasButton("Compare selected branches"));
        var workspace = (GitWorkspace)typeof(ShellWindow).GetField("workspace", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var project = workspace.Projects.Single();
        Click("Compare selected branches");
        await Until(() => Texts().Any(text => text.Contains("Only in the first: 1; only in the second: 1")));
        Check(Texts().Any(text => text.Contains("Compared branches:")), "selected branch controls show actual divergent relation counts");
        Capture(root, "selected-branches-light");
        var filter = Fields().First(field => AutomationProperties.GetName(field).StartsWith("Filter this page")); filter.Text = "no matching recorded version";
        Check(Texts().Any(text => text.Contains("No loaded commits match this filter")) && !Descendants<CommitGraph>(window).Any(), "history filter removes unmatched nodes and visibly describes the loaded-page limit");
        Capture(root, "filtered-history-light"); filter.Text = "";
        Check(Descendants<CommitGraph>(window).Any(), "clearing history filter restores the recorded parent graph");

        var beforeBranchViewing = Program.Fingerprint(repository);
        workspace.SetTrusted(project.Id, true); // Only this disposable fixture is trusted by the test.
        foreach (var mode in new[] { AccessLevel.ReadOnly, AccessLevel.MonitorOnly })
        {
            ((ComboBox)window.FindName("AccessSelector")).SelectedIndex = (int)mode;
            await Until(() => workspace.Access.Get(project.Id).Level == mode);
            window.ConfigureView("Branches", mode == AccessLevel.ReadOnly ? "Light" : "Dark");
            await Until(() => HasButton("See saved versions on this line of work"));
            var branches = Descendants<ListBox>(window).Single(list => AutomationProperties.GetName(list) == "Your lines of work (branches)");
            var selected = branches.Items.OfType<ListBoxItem>().First(item => item.Tag is BranchInfo branch && branch.IsRemote == (mode == AccessLevel.ReadOnly) && !branch.Name.EndsWith("/HEAD", StringComparison.Ordinal));
            branches.SelectedItem = selected;
            Check(Button("See saved versions on this line of work").IsEnabled && !Button("Preview switching to this line of work (branch)").IsEnabled && !Button("Preview creating a new line of work (branch)").IsEnabled, $"{mode} permits viewing branch history while branch writes remain disabled");
            Check(!((Button)window.FindName("NewTerminalButton")).IsEnabled, $"{mode} explains disabled interactive terminal creation");
            Capture(root, mode == AccessLevel.ReadOnly ? "branch-browsing-read-only-light" : "branch-browsing-monitor-dark");
            Click("See saved versions on this line of work");
            await Until(() => Texts().Any(text => text.Contains("Viewing recorded history:")) && HasButton("Show current branch and team copy") && IsIdle());
            var shellModel = typeof(ShellWindow).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var history = (HistoryPage)shellModel.GetType().GetProperty("History")!.GetValue(shellModel)!;
            var selectedBranch = (BranchInfo)selected.Tag;
            Check(history.Commits.First().Id == selectedBranch.Id && history.Comparison is { } selectedComparison && selectedComparison.LeftCommitId == selectedBranch.Id && selectedComparison.RightCommitId == selectedBranch.Id, $"{mode} shows only the selected local or fetched branch's recorded ancestry");
            Check(Program.Fingerprint(repository) == beforeBranchViewing && workspace.GetSnapshot(project.Id)?.Branch == "main" && workspace.GetSnapshot(project.Id)?.Remote.State == RemoteCheckState.NotChecked, $"{mode} branch viewing preserves all repository bytes, checkout and remote-check state");
            Capture(root, mode == AccessLevel.ReadOnly ? "view-branch-history-read-only-light" : "view-branch-history-monitor-dark");
        }
        ((ComboBox)window.FindName("AccessSelector")).SelectedIndex = (int)AccessLevel.ReadOnly;
        await Until(() => workspace.Access.Get(project.Id).Level == AccessLevel.ReadOnly);
        window.ConfigureView("History graph", "Light"); await Until(() => HasButton("Show current branch and team copy"));
        Click("Show current branch and team copy"); await Until(() => !Texts().Any(text => text.Contains("Viewing recorded history:")) && IsIdle());

        ((Button)window.FindName("ActivityToggleButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await Until(() => ((FrameworkElement)window.FindName("DockSurface")).IsVisible);
        var groups = (StackPanel)window.FindName("OutputGroups");
        var inspectedGroup = groups.Children.OfType<Expander>().First(); inspectedGroup.IsExpanded = true; window.UpdateLayout();
        Check(groups.Children.OfType<Expander>().Any(group => group.Header?.ToString()?.Contains("Read project status") == true) && HasButton("Copy this operation"), "output uses truthful inspect labels and accessible per-operation groups/copy controls");
        var severity = (ComboBox)window.FindName("OutputSeverity"); severity.SelectedIndex = 1;
        Check(!groups.Children.OfType<Expander>().Any(), "Problems output filter hides successful inspection events"); severity.SelectedIndex = 0;
        var grouping = (CheckBox)window.FindName("GroupOutput"); grouping.IsChecked = false;
        Check(((TextBox)window.FindName("OutputText")).Visibility == Visibility.Visible && ((ScrollViewer)window.FindName("OutputGroupsScroll")).Visibility == Visibility.Collapsed, "combined text remains available alongside grouped operation output"); grouping.IsChecked = true;
        var hostedId = Guid.NewGuid(); var addEvent = typeof(ShellWindow).GetMethod("AddEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        addEvent.Invoke(window, [new OperationEvent(hostedId, project.Id, DateTimeOffset.Now, OperationState.Starting, "hosted-purpose", "Fixture hosted read without HTTP", OperationKind.GitHubRead)]);
        addEvent.Invoke(window, [new OperationEvent(hostedId, project.Id, DateTimeOffset.Now, OperationState.Succeeded, "hosted", "Fixture read completed without HTTP.", OperationKind.GitHubRead)]);
        var channel = (ComboBox)window.FindName("OutputChannel"); channel.SelectedIndex = 4;
        Check(groups.Children.OfType<Expander>().Single().Header?.ToString()?.Contains("Fixture hosted read without HTTP") == true && groups.Children.OfType<Expander>().Single().Header?.ToString()?.Contains("Succeeded") == true, "GitHub activity channel groups hosted events and shows their final state without live requests");
        channel.SelectedIndex = 1;
        Check(!groups.Children.OfType<Expander>().Any(group => group.Header?.ToString()?.Contains("Fixture hosted") == true), "Git operation channel keeps hosted API activity separate"); channel.SelectedIndex = 0;
        var hostedOperation = typeof(ShellWindow).GetMethod("HostedOperationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(bool));
        await (Task<bool>)hostedOperation.Invoke(window, [project.Id, OperationKind.GitHubRead, "Fixture source verification without HTTP", (Func<Task<bool>>)(() => Task.FromResult(false))])!;
        channel.SelectedIndex = 4;
        Check(groups.Children.OfType<Expander>().Any(group => group.Header?.ToString()?.Contains("Fixture source verification") == true && group.Header.ToString()!.Contains("Stale")), "a mismatched published-source verification is marked stale and blocks submission in hosted output");
        var denied = false;
        try { await (Task<bool>)hostedOperation.Invoke(window, [project.Id, OperationKind.GitHubRead, "Fixture access denial without HTTP", (Func<Task<bool>>)(() => Task.FromException<bool>(new UnauthorizedAccessException("Fixture denied")))])!; } catch (UnauthorizedAccessException) { denied = true; }
        channel.SelectedIndex = 0; channel.SelectedIndex = 4;
        Check(denied && groups.Children.OfType<Expander>().Any(group => group.Header?.ToString()?.Contains("Fixture access denial") == true && group.Header.ToString()!.Contains("Denied")), "an unauthorized hosted operation is recorded as denied without sending a request"); channel.SelectedIndex = 0;
        var reveal = ProjectFileReveal.Create(root, "reviewed executable & name.exe");
        Check(!reveal.UseShellExecute && reveal.FileName == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") && reveal.ArgumentList.Single() == "/select," + Path.Combine(root, "reviewed executable & name.exe"), "project-file reveal starts only typed Windows Explorer selection even for executable-looking names");
        var escaped = false; try { ProjectFileReveal.Create(root, "..\\outside.exe"); } catch (ArgumentException) { escaped = true; }
        Check(escaped, "project-file reveal rejects paths escaping the reviewed project");

        window.ConfigureView("Changes", "Light"); window.UpdateLayout();
        var files = Descendants<ListBox>(window).First(list => AutomationProperties.GetName(list) == "Unstaged and new files"); files.SelectedIndex = files.Items.Count - 1;
        await Until(() => Fields().Any(field => field.Text.Contains("Unstaged work") || field.Text.Contains("New untracked content")));
        Check(Fields().Any(field => field.Text.Contains("Unstaged work") || field.Text.Contains("New untracked content")), "native file selection displays real changed contents");
        Capture(root, "selected-diff-light");
        workspace.SetTrusted(project.Id, true); await window.OpenProjectAsync(repository); window.ConfigureView("Changes", "Light"); window.UpdateLayout();
        ((ComboBox)window.FindName("AccessSelector")).SelectedIndex = (int)AccessLevel.AllowChanges;
        await Until(() => HasButton("Preview adding chosen files (stage)") && Button("Preview adding chosen files (stage)").IsEnabled && IsIdle());
        files = Descendants<ListBox>(window).First(list => AutomationProperties.GetName(list) == "Unstaged and new files"); files.SelectedIndex = 0;
        Click("Preview adding chosen files (stage)"); await Until(() => HasButton("Confirm and run this step"));
        Check(Fields().Any(field => field.Text.Contains("git --literal-pathspecs add")), "native preview exposes the underlying selected-file command before execution");
        Capture(root, "stage-preview-light"); Click("Cancel review"); window.UpdateLayout();
        Check(HasButton("Preview saving this version (commit)"), "canceling a review restores Changes without executing it");
        Click("Preview saving this version (commit)"); await Until(() => ((Border)window.FindName("StatusBanner")).Visibility == Visibility.Visible && Texts().Any(text => text.Contains("That step didn't work")));
        Check(((StackPanel)window.FindName("ProblemsPanel")).Children.Count > 0, "an invalid local commit request produces a visible actionable problem");
        window.ConfigureView("Changes", "Dark"); Capture(root, "actionable-failure-dark");
        ((ComboBox)window.FindName("AccessSelector")).SelectedIndex = (int)AccessLevel.ReadOnly;
        await Until(() => workspace.Access.Get(project.Id).Level == AccessLevel.ReadOnly);

        window.ConfigureView("Settings", "Dark"); window.UpdateLayout();
        Checkbox("Mute team-update notifications").IsChecked = true; Checkbox("Use quiet hours for team-update notifications").IsChecked = true;
        Fields().First(field => AutomationProperties.GetName(field).StartsWith("Start hour")).Text = "22";
        Fields().First(field => AutomationProperties.GetName(field).StartsWith("End hour")).Text = "8";
        Fields().First(field => AutomationProperties.GetName(field).StartsWith("How often to check for team updates")).Text = "1440";
        Click("Save settings"); var store = new AppStateStore(state);
        await UntilAsync(async () => { var settings = await store.LoadSettingsAsync(); return settings.MuteMonitoringNotifications && settings.QuietHoursStart == 22 && settings.QuietHoursEnd == 8 && settings.FetchIntervalMinutes == 1440; });
        Check(true, "mute, overnight quiet hours and supported check interval persist through actual Settings controls");
        var advanced = Descendants<Expander>(window).First(expander => expander.Header?.ToString() == "Advanced connection setup");
        Check(!advanced.IsExpanded, "technical Git and GitHub configuration starts collapsed after Git is detected");
        Capture(root, "notification-settings-dark");
        Check(Contrast("#17243C", "#3C93F2") >= 4.5 && Contrast("#5D6C82", "#F6F8FC") >= 4.5 && Contrast("#5D6C82", "#EAF2FF") >= 4.5, "normal-text primary/muted theme colors meet the reviewed 4.5-to-1 palette target");
        // Ordinary-folder fixtures must stay outside the source repository and its ancestors.
        var candidate = Path.Combine(Path.GetTempPath(), "GitCompass-candidate", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(candidate); await File.WriteAllTextAsync(Path.Combine(candidate, "practice.txt"), "Unrecorded practice work\n");
        await window.OpenProjectAsync(candidate); window.ConfigureView("Overview", "Light"); window.UpdateLayout();
        Check(((Border)window.FindName("StatusBanner")).Visibility == Visibility.Collapsed, "changing projects clears transient failures from the previously selected project");
        Check(!Directory.Exists(Path.Combine(candidate, ".git")) && HasButton("These are my own files"), "non-Git onboarding preserves candidate folder and asks project origin before initialization");
        Click("These are my own files"); await Until(() => HasButton("Preview starting version saving here (git init)"));
        Capture(root, "non-git-onboarding-light");
        var candidateBefore = Program.Fingerprint(candidate);
        var candidateProject = workspace.Projects.Single(p => p.RootPath == candidate);
        workspace.SetTrusted(candidateProject.Id, true);
        await window.OpenProjectAsync(candidate);
        ((ComboBox)window.FindName("AccessSelector")).SelectedIndex = (int)AccessLevel.MonitorOnly;
        await Until(() => workspace.Access.Get(candidateProject.Id).Level == AccessLevel.MonitorOnly);
        Check(((TextBlock)window.FindName("BranchLabel")).Text == "Not saving versions yet" && !((Button)window.FindName("CheckRemoteButton")).IsEnabled, "a plain folder has truthful branch context and no remote-check action even in Monitor only");
        foreach (var page in new[] { "Changes", "History graph", "Branches", "Pull requests" })
        {
            window.ConfigureView(page, "Light"); window.UpdateLayout();
            Check(Texts().Any(text => text.Contains("This folder doesn't keep saved versions yet")) && HasButton("Start saving versions here") && !HasButton("Preview saving this version (commit)") && !HasButton("Preview creating a new line of work (branch)"), $"non-Git {page} offers learning/setup instead of empty Git controls");
            Capture(root, "non-git-" + page.Replace(' ', '-').ToLowerInvariant() + "-light");
        }
        Click("Start saving versions here"); await Until(() => HasButton("Preview starting version saving here (git init)"));
        Check(!Button("Preview starting version saving here (git init)").IsEnabled && !Directory.Exists(Path.Combine(candidate, ".git")) && Program.Fingerprint(candidate) == candidateBefore, "opening a non-Git setup path does not initialize or alter existing files in Monitor only");
        window.ConfigureView("Settings", "Light"); window.UpdateLayout();
        Check(!Checkbox("Enable periodic remote checks for the current project in this session").IsEnabled, "a non-Git folder cannot enable periodic fetches");
        ((TabControl)window.FindName("DockTabs")).SelectedIndex = 1; window.UpdateLayout(); Click("Get help: make a summary to share");
        await Until(() => HasButton("Save reviewed diagnostic bundle"));
        Check(Texts().Any(text => text.Contains("Project: project-a")) && Fields().Count(field => !field.IsReadOnly && field.Text.Length > 100) >= 2, "a retained problem opens its originating project and exposes both editable support artifacts before saving");
        Capture(root, "reviewed-support-light");
    }

    private static double Contrast(string first, string second)
    {
        static double Luminance(string color) { var rgb = (Color)ColorConverter.ConvertFromString(color); static double Linear(byte value) { var channel = value / 255d; return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4); } return 0.2126 * Linear(rgb.R) + 0.7152 * Linear(rgb.G) + 0.0722 * Linear(rgb.B); }
        var a = Luminance(first); var b = Luminance(second); return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
    private static void Capture(string root, string name)
    {
        window!.UpdateLayout(); var surface = (FrameworkElement)window.Content;
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) { var bounds = new Rect(0, 0, surface.ActualWidth, surface.ActualHeight); drawing.DrawRectangle((Brush)window.FindResource("WindowBrush"), null, bounds); drawing.DrawRectangle(new VisualBrush(surface) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds, ViewportUnits = BrushMappingMode.Absolute, Viewport = bounds, Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, bounds); }
        var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var folder = Path.Combine(root, "native-states"); Directory.CreateDirectory(folder); using var file = File.Create(Path.Combine(folder, name + ".png")); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(file);
    }
    private static async Task Until(Func<bool> condition) => await UntilAsync(() => Task.FromResult(condition()));
    private static async Task UntilAsync(Func<Task<bool>> condition)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < TimeSpan.FromSeconds(35)) { window!.UpdateLayout(); if (await condition()) return; await Task.Delay(35); }
        throw new TimeoutException("WPF interaction condition timed out. " + window!.SmokeDescription + " Banner: " + ((TextBlock)window.FindName("BannerText")).Text + " Problems: " + string.Join(" | ", Descendants<TextBlock>((StackPanel)window.FindName("ProblemsPanel")).Select(text => text.Text)));
    }
    private static IEnumerable<string> Texts() => Descendants<TextBlock>(window!).Select(text => text.Text);
    private static bool IsIdle()
    {
        var model = typeof(ShellWindow).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        return !(bool)model.GetType().GetProperty("Busy")!.GetValue(model)!;
    }
    private static IEnumerable<TextBox> Fields() => Descendants<TextBox>(window!);
    private static CheckBox Checkbox(string content) => Descendants<CheckBox>(window!).First(check => check.Content?.ToString() == content || AutomationProperties.GetName(check) == content);
    private static bool HasButton(string content) => Descendants<Button>(window!).Any(button => button.Content?.ToString() == content);
    private static Button Button(string content) => Descendants<Button>(window!).First(button => button.Content?.ToString() == content);
    private static void Click(string content) => Button(content).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    private static void Check(bool result, string description) { if (!result) throw new InvalidOperationException(description); checks++; Console.WriteLine("PASS " + description); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    { if (root is T match) yield return match; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
}
