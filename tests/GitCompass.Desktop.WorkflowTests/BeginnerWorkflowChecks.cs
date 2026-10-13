using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GitCompass.Application;
using GitCompass.Desktop;
using GitCompass.Domain;
using GitCompass.Infrastructure.Local;

internal static class BeginnerWorkflowChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static async Task RunAppearanceOnlyAsync(ShellWindow window, Action<bool, string> check)
    {
        var store = Field<AppStateStore>(window, "store");
        window.ConfigureView("Settings", "Light");
        var theme = Appearance(window);
        theme.SelectedItem = "Dark"; await UntilAsync(window, async () => (await store.LoadSettingsAsync()).Theme == "Dark");
        check(((SolidColorBrush)window.FindResource("WindowBrush")).Color.R < 100, "focused shell saves and applies Dark immediately");
        var restart = new ShellWindow(store.Root) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            restart.Show(); await restart.InitializeAsync(); restart.ConfigureView("Settings", null);
            var secondTheme = Appearance(restart);
            check(secondTheme.SelectedItem?.ToString() == "Dark" && ((SolidColorBrush)restart.FindResource("WindowBrush")).Color.R < 100, "focused appearance restart restores saved dark");
        }
        finally
        {
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); restart.Closed += (_, _) => closed.SetResult(); restart.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        theme = Appearance(window);
        check(AttachedTo(window, theme) && theme.SelectedItem?.ToString() == "Dark", "focused theme changes target the current attached Settings control");
        theme.SelectedItem = "Light";
        await UntilAsync(window, async () => (await store.LoadSettingsAsync()).Theme == "Light");
        check(theme.SelectedItem?.ToString() == "Light" && ((SolidColorBrush)window.FindResource("WindowBrush")).Color.R > 200, "focused original shell saves and applies Light after second shell closes");
        theme.SelectedItem = "System"; await UntilAsync(window, async () => (await store.LoadSettingsAsync()).Theme == "System");
        check((await store.LoadSettingsAsync()).Theme == "System", "focused original shell saves System after Light");
    }

    public static async Task RunAsync(ShellWindow window, string evidenceRoot, Action<bool, string> check)
    {
        var workspace = Field<GitWorkspace>(window, "workspace");
        var originalRepository = Snapshot(window)!.Project.RootPath;
        var state = Field<AppStateStore>(window, "store");
        var root = Path.Combine(Path.GetTempPath(), "GitCompass-beginner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var repository = Path.Combine(root, "My ordinary project"); Directory.CreateDirectory(repository);
        var keep = "saved-notes.txt"; var leave = "unfinished-notes.txt";
        await File.WriteAllTextAsync(Path.Combine(repository, keep), "My first selected version\n");
        await File.WriteAllTextAsync(Path.Combine(repository, leave), "This file stays outside my first version\n");
        await window.OpenProjectAsync(repository); window.ConfigureView("Overview", "Light");
        check(Snapshot(window)!.Project.Kind == RepositoryKind.Candidate && !Directory.Exists(Path.Combine(repository, ".git")), "ordinary project detection does not create Git or alter its files");
        check(HasButton(window, "These are my own files") && HasButton(window, "I got a team copy or project link") && !HasButton(window, "Review initialization"), "ordinary-folder setup asks project intent before technical initialization");
        Capture(window, evidenceRoot, "01-ordinary-folder-light");
        Click(window, "I got a team copy or project link");
        await Until(window, () => Fields(window).Any(box => AutomationProperties.GetName(box) == "Team project link") && IsIdle(window));
        check(HasButton(window, "Choose a separate empty download folder") && !HasButton(window, "Review initialization") && !HasButton(window, "Review download of team project") && !Directory.Exists(Path.Combine(repository, ".git")), "team-copy intent preserves the populated original and requires a separate empty download destination");
        Capture(window, evidenceRoot, "01b-team-copy-light");
        Click(window, "These are my own files");
        await Until(window, () => HasButton(window, "Review initialization") && IsIdle(window));
        check(!Button(window, "Review initialization").IsEnabled && Texts(window).Any(text => text.Contains("Allow changes")), "own-files walkthrough explains and enforces Read-only initialization");
        check((await state.LoadBeginnerGuideAsync()) is { Active: true, Intent: "OwnFiles" } own && own.ProjectFolder == repository, "selected ordinary project and own-files intent are saved for resuming");
        Capture(window, evidenceRoot, "02-own-files-read-only-light");

        await Unlock(window, workspace);
        Click(window, "Review initialization"); await Until(window, () => HasButton(window, "Run reviewed operation") && IsIdle(window));
        check(Fields(window).Any(box => box.Text.Contains("git init --initial-branch")), "beginner initialization reaches the central review with its actual Git command");
        Click(window, "Run reviewed operation");
        await Until(window, () => Snapshot(window)?.Project.Kind == RepositoryKind.WorkingTree && IsIdle(window));
        check(Directory.Exists(Path.Combine(repository, ".git")) && Snapshot(window)!.HeadId is null && await Git(repository, "symbolic-ref", "--short", "HEAD") == "main\n", "reviewed initialization creates main and preserves the unborn state before any version is saved");
        await Git(repository, "config", "--local", "core.autocrlf", "false"); await Git(repository, "config", "--local", "commit.gpgsign", "false");

        // Local empty values deliberately override any global author. The test never changes global config.
        await Git(repository, "config", "--local", "user.name", ""); await Git(repository, "config", "--local", "user.email", "");
        await window.OpenProjectAsync(repository); window.ConfigureView("Walkthrough", "Light");
        check(!Snapshot(window)!.HasAuthorIdentity && HasButton(window, "Set author details first"), "missing author details are explained before attempting the first commit");
        Click(window, "Set author details first");
        await Until(window, () => Fields(window).Any(box => AutomationProperties.GetName(box) == "Your author name"));
        ((ComboBox)window.FindName("AccessSelector")).SelectedIndex = (int)AccessLevel.ReadOnly;
        await Until(window, () => workspace.Access.Get(Snapshot(window)!.ProjectId).Level == AccessLevel.ReadOnly && HasButton(window, "Review author identity change") && !Button(window, "Review author identity change").IsEnabled && IsIdle(window));
        check(!Button(window, "Review author identity change").IsEnabled, "author setup also respects the current project access level");
        await Unlock(window, workspace);
        FieldByName(window, "Your author name").Text = "Beginner workflow fixture";
        FieldByName(window, "Your author email").Text = "beginner-fixture@example.invalid";
        Capture(window, evidenceRoot, "03-author-setup-light");
        Click(window, "Review author identity change"); await Until(window, () => HasButton(window, "Run reviewed operation") && IsIdle(window));
        check(Fields(window).Any(box => box.Text.Contains("config --local user.name")), "author form uses reviewed repository-local configuration");
        Click(window, "Run reviewed operation"); await Until(window, () => Snapshot(window)?.HasAuthorIdentity == true && HasButton(window, "Back to Changed files") && IsIdle(window));
        check(await Git(repository, "config", "--local", "user.name") == "Beginner workflow fixture\n" && await Git(repository, "config", "--local", "user.email") == "beginner-fixture@example.invalid\n", "reviewed author details are stored only in the disposable project");
        Click(window, "Back to Changed files");
        await Until(window, () => HasButton(window, "Review staging selected files"));
        var list = Descendants<ListBox>(window).Single(item => AutomationProperties.GetName(item) == "Unstaged and new files");
        var chosen = list.Items.OfType<ListBoxItem>().Single(item => item.Tag is FileState file && file.Path == keep);
        var checkbox = Descendants<CheckBox>(chosen).Single(); checkbox.IsChecked = true;
        check(list.SelectedItems.Count == 1 && chosen.IsSelected, "native file checkbox selects exactly the one intended path");
        Click(window, "Review staging selected files"); await Until(window, () => HasButton(window, "Run reviewed operation") && IsIdle(window));
        Click(window, "Run reviewed operation");
        await Until(window, () => Snapshot(window)?.Files.Count(file => file.IsStaged) == 1 && HasButton(window, "Review local commit") && IsIdle(window));
        check(await Git(repository, "diff", "--cached", "--name-only") == keep + "\n", "reviewed beginner staging includes only the selected file");
        FieldByPrefix(window, "Commit message").Text = "My first guided local version";
        Click(window, "Review local commit"); await Until(window, () => HasButton(window, "Run reviewed operation") && IsIdle(window));
        Capture(window, evidenceRoot, "04-local-commit-review-light");
        Click(window, "Run reviewed operation");
        await Until(window, () => Snapshot(window)?.HeadId is not null && HasButton(window, "Review local commit") && IsIdle(window));
        var head = (await Git(repository, "rev-parse", "HEAD")).Trim();
        check(await Git(repository, "ls-tree", "--name-only", "HEAD") == keep + "\n" && await Git(repository, "show", "HEAD:" + keep) == "My first selected version\n", "actual first HEAD contains the chosen contents without the unselected file");
        check(Snapshot(window)!.Files.Any(file => file.Path == leave && file.Kind == FileKind.Untracked) && await File.ReadAllTextAsync(Path.Combine(repository, leave)) == "This file stays outside my first version\n", "the unselected file remains untouched and uncommitted");
        check((await Git(repository, "remote")).Length == 0 && Texts(window).Any(text => text.Contains("has not uploaded")), "successful local-save result explains that this step did not upload the version");
        window.ConfigureView("Walkthrough", "Dark");
        check(Texts(window).Any(text => text.Contains("6. Your version is saved on this computer")) && Texts(window).Any(text => text.Contains(head[..8])), "walkthrough completion displays the actual saved commit ID");
        check((await state.LoadBeginnerGuideAsync()).SavedCommitId == head, "walkthrough records only the real successful commit for its current project");
        Capture(window, evidenceRoot, "05-first-version-complete-dark");

        Click(window, "Pause walkthrough"); await Until(window, () => HasButton(window, "Guide me through a local save") && IsIdle(window));
        check((await state.LoadBeginnerGuideAsync()) is { Active: false, SavedCommitId: not null } paused && paused.SavedCommitId == head, "pausing persists completion while leaving the walkthrough optional");
        Click(window, "Guide me through a local save"); await Until(window, () => HasButton(window, "Pause walkthrough"));
        check((await state.LoadBeginnerGuideAsync()).Active, "resuming keeps the guide active without discarding the saved version");

        var restart = new ShellWindow(state.Root) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            restart.Show(); await restart.InitializeAsync(); restart.ConfigureView("Overview", "Light");
            check(HasButton(restart, "Resume walkthrough"), "a fresh shell offers the persisted walkthrough without reopening files automatically");
            Click(restart, "Resume walkthrough");
            await Until(restart, () => Snapshot(restart)?.Project.RootPath == repository && HasButton(restart, "Pause walkthrough") && IsIdle(restart));
            check(Snapshot(restart)!.HeadId == head && Texts(restart).Any(text => text.Contains("6. Your version is saved on this computer")), "native resume after restart re-inspects the actual project and restores its completed version");
            check(Field<GitWorkspace>(restart, "workspace").Access.Get(Snapshot(restart)!.ProjectId).Level == AccessLevel.ReadOnly, "resume never restores the previous session's write permission");
            Capture(restart, evidenceRoot, "06-resumed-guide-light");
        }
        finally
        {
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); restart.Closed += (_, _) => closed.SetResult(); restart.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        await File.AppendAllTextAsync(Path.Combine(repository, keep), "A later version outside the walkthrough\n");
        await Git(repository, "add", "--", keep); await Git(repository, "commit", "-m", "Fixture later version");
        await window.OpenProjectAsync(repository);
        var model = Field<object>(window, "model");
        model.GetType().GetProperty("History")!.SetValue(model, new HistoryPage([], false, false, null));
        window.ConfigureView("Walkthrough", "Light");
        check(Snapshot(window)!.HeadId != head && Texts(window).Any(text => text.Contains("Earlier walkthrough save: " + head[..8])) && HasButton(window, "Finish walkthrough") && (await state.LoadBeginnerGuideAsync()).SavedCommitId == head,
            "a later HEAD and unrelated displayed history never erase the recorded successful walkthrough save");

        var protectedFiles = new[] { Path.Combine(repository, keep), Path.Combine(repository, leave), Path.Combine(repository, ".git", "config"), Path.Combine(repository, ".git", "HEAD"), Path.Combine(repository, ".git", "index") };
        var bytes = protectedFiles.ToDictionary(path => path, File.ReadAllBytes);
        var directories = Directory.GetDirectories(repository).Order().ToArray();
        await InvokeAsync(window, "CreatePracticeAtAsync", repository);
        check(protectedFiles.All(path => File.ReadAllBytes(path).SequenceEqual(bytes[path])) && directories.SequenceEqual(Directory.GetDirectories(repository).Order()), "practice creation rejects an existing repository without changing files, configuration or index");
        await InvokeAsync(window, "CreatePracticeAtAsync", root);
        var practice = Snapshot(window)!.Project.RootPath;
        check(practice != repository && Path.GetDirectoryName(practice) == root && File.Exists(Path.Combine(practice, "Learning-notes.txt")) && !Directory.Exists(Path.Combine(practice, ".git")), "practice creates a separate sample folder without implicitly initializing Git");
        check((await state.LoadBeginnerGuideAsync()).PracticeFolder == practice && (await state.LoadBeginnerGuideAsync()).SavedCommitId is null, "practice state is persisted without inventing a completed version");
        Capture(window, evidenceRoot, "07-practice-folder-light");

        await window.OpenProjectAsync(originalRepository); window.ConfigureView("Walkthrough", "Light");
        check(Snapshot(window)!.HeadId is not null && (await state.LoadBeginnerGuideAsync()).SavedCommitId is null && !Texts(window).Any(text => text.Contains("6. Your version is saved on this computer")), "opening a project with existing history does not fabricate first-save completion");
        var search = (TextBox)window.FindName("NavigationSearch"); search.Text = "theme";
        var navigation = (ListBox)window.FindName("Navigation");
        check(navigation.Items.OfType<ListBoxItem>().Single(item => item.Visibility == Visibility.Visible).Tag?.ToString() == "Settings", "native navigation search finds appearance settings using everyday wording");
        search.Text = "no page matches fixture";
        check(((TextBlock)window.FindName("NavigationSearchSummary")).Visibility == Visibility.Visible, "navigation search explains when no page matches"); search.Text = "";
        window.ConfigureView("Settings", "Light");
        window.UpdateLayout();
        var themes = Descendants<ComboBox>(window).Single(item => AutomationProperties.GetName(item) == "Appearance theme"); themes.SelectedItem = "Dark";
        await UntilAsync(window, async () => (await state.LoadSettingsAsync()).Theme == "Dark");
        check(((SolidColorBrush)window.FindResource("WindowBrush")).Color.R < 100, "native appearance control applies the dark palette throughout the shell");
        themes = Appearance(window);
        check(AttachedTo(window, themes) && themes.SelectedItem?.ToString() == "Dark", "dropdown coverage operates on the actual attached dark appearance control");
        themes.ApplyTemplate(); themes.IsDropDownOpen = true;
        await Until(window, () => themes.Template.FindName("PART_Popup", themes) is Popup { IsOpen: true });
        var popup = (Popup)themes.Template.FindName("PART_Popup", themes)!;
        await Until(window, () =>
        {
            popup.Child?.UpdateLayout();
            return themes.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated &&
                Enumerable.Range(0, themes.Items.Count).All(index => themes.ItemContainerGenerator.ContainerFromIndex(index) is ComboBoxItem);
        });
        check(popup.IsOpen && themes.Items.Cast<string>().SequenceEqual(new[] { "System", "Light", "Dark" }) &&
            Enumerable.Range(0, themes.Items.Count).All(index => themes.ItemContainerGenerator.ContainerFromIndex(index) is ComboBoxItem choice && choice.Content?.ToString() == themes.Items[index]?.ToString()),
            "Fluent appearance dropdown opens its native popup and realizes all three theme choices");
        Capture(window, evidenceRoot, "08-settings-dark"); themes.IsDropDownOpen = false;
        window.ConfigureView("Learn", null); window.ConfigureView("Settings", null); window.UpdateLayout();
        themes = Descendants<ComboBox>(window).Single(item => AutomationProperties.GetName(item) == "Appearance theme");
        check(themes.SelectedItem?.ToString() == "Dark" && ((SolidColorBrush)window.FindResource("WindowBrush")).Color.R < 100, "appearance selection persists immediately and survives navigation away and back");
        var themeRestart = new ShellWindow(state.Root) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            themeRestart.Show(); await themeRestart.InitializeAsync(); themeRestart.ConfigureView("Settings", null); themeRestart.UpdateLayout();
            var restoredTheme = Descendants<ComboBox>(themeRestart).Single(item => AutomationProperties.GetName(item) == "Appearance theme");
            check(restoredTheme.SelectedItem?.ToString() == "Dark" && ((SolidColorBrush)themeRestart.FindResource("WindowBrush")).Color.R < 100,
                "fresh startup restores the saved dark selection and actual shell palette");
        }
        finally
        {
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); themeRestart.Closed += (_, _) => closed.SetResult(); themeRestart.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        themes = Appearance(window);
        check(AttachedTo(window, themes) && themes.SelectedItem?.ToString() == "Dark", "after restart closes, theme changes target the current attached Settings control");
        themes.SelectedItem = "Light";
        await UntilAsync(window, async () => (await state.LoadSettingsAsync()).Theme == "Light");
        check(((SolidColorBrush)window.FindResource("WindowBrush")).Color.R > 200 && themes.Items.Cast<string>().Contains("System"), "native appearance control restores light and offers the Windows system theme");
        themes = Appearance(window);
        check(AttachedTo(window, themes) && themes.SelectedItem?.ToString() == "Light", "system selection starts from the currently attached light appearance control");
        themes.SelectedItem = "System";
        await UntilAsync(window, async () => (await state.LoadSettingsAsync()).Theme == "System");
        check((await state.LoadSettingsAsync()).Theme == "System", "system theme choice is saved without changing Windows preferences");
        var dock = (RowDefinition)window.FindName("DockRow"); check(dock.Height.Value == 0, "activity tools start collapsed so the next beginner action has space");
        ((Button)window.FindName("ActivityToggleButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); check(dock.Height.Value > 0, "activity details remain available when deliberately expanded");
        ((Button)window.FindName("ActivityToggleButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Console.WriteLine("Beginner fixture: " + root + ". Rendered evidence: " + evidenceRoot);
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static ComboBox Appearance(ShellWindow window) { window.UpdateLayout(); return Descendants<ComboBox>(window).Single(item => AutomationProperties.GetName(item) == "Appearance theme"); }
    private static bool AttachedTo(ShellWindow window, FrameworkElement control) => control.IsLoaded && Window.GetWindow(control) == window && PresentationSource.FromVisual(control) is not null;
    private static RepositorySnapshot? Snapshot(ShellWindow window) => typeof(ShellWindow).GetField("model", Private)!.GetValue(window)!.GetType().GetProperty("Snapshot")!.GetValue(Field<object>(window, "model")) as RepositorySnapshot;
    private static bool IsIdle(ShellWindow window) => !(bool)Field<object>(window, "model").GetType().GetProperty("Busy")!.GetValue(Field<object>(window, "model"))!;
    private static IEnumerable<TextBox> Fields(ShellWindow window) { window.UpdateLayout(); return Descendants<TextBox>(window); }
    private static IEnumerable<string> Texts(ShellWindow window) { window.UpdateLayout(); return Descendants<TextBlock>(window).Select(text => text.Text); }
    private static TextBox FieldByName(ShellWindow window, string name) => Fields(window).Single(box => AutomationProperties.GetName(box) == name);
    private static TextBox FieldByPrefix(ShellWindow window, string prefix) => Fields(window).Single(box => AutomationProperties.GetName(box).StartsWith(prefix));
    private static bool HasButton(ShellWindow window, string label) { window.UpdateLayout(); return Descendants<Button>(window).Any(button => button.Content?.ToString() == label); }
    private static Button Button(ShellWindow window, string label) { window.UpdateLayout(); return Descendants<Button>(window).Single(button => button.Content?.ToString() == label); }
    private static void Click(ShellWindow window, string label) => Button(window, label).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    private static async Task InvokeAsync(ShellWindow window, string name, params object[] arguments) => await (Task)typeof(ShellWindow).GetMethod(name, Private)!.Invoke(window, arguments)!;
    private static async Task Unlock(ShellWindow window, GitWorkspace workspace)
    {
        var page = (string)Field<object>(window, "model").GetType().GetProperty("Page")!.GetValue(Field<object>(window, "model"))!;
        var id = Snapshot(window)!.ProjectId; workspace.SetTrusted(id, true);
        await window.OpenProjectAsync(Snapshot(window)!.Project.SelectedFolder);
        window.ConfigureView(page, null);
        ((ComboBox)window.FindName("AccessSelector")).SelectedIndex = (int)AccessLevel.AllowChanges;
        var operation = page == "Author setup" ? "Review author identity change" : "Review initialization";
        await Until(window, () => workspace.Access.Get(id).Level == AccessLevel.AllowChanges && HasButton(window, operation) && Button(window, operation).IsEnabled && IsIdle(window));
    }
    private static async Task Until(ShellWindow window, Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(45))
        { window.UpdateLayout(); try { if (condition()) return; } catch (InvalidOperationException) { } await Task.Delay(30); }
        throw new TimeoutException("Beginner native condition failed: " + window.SmokeDescription);
    }
    private static async Task UntilAsync(ShellWindow window, Func<Task<bool>> condition)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(10))
        { window.UpdateLayout(); if (await condition()) return; await Task.Delay(30); }
        throw new TimeoutException("Beginner persisted-setting condition failed: " + window.SmokeDescription);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static void Capture(ShellWindow window, string root, string name)
    {
        window.UpdateLayout(); Directory.CreateDirectory(root);
        var surface = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) { var bounds = new Rect(0, 0, surface.ActualWidth, surface.ActualHeight); drawing.DrawRectangle(window.Background, null, bounds); drawing.DrawRectangle(new VisualBrush(surface), null, bounds); }
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(root, name + ".png")); encoder.Save(file);
        File.WriteAllText(Path.Combine(root, name + ".txt"), window.SmokeDescription);
    }
    private static async Task<string> Git(string repository, params string[] arguments)
    {
        var executable = DependencyProbe.FindGitExecutable() ?? throw new FileNotFoundException("Git is required for beginner fixtures.");
        var start = new ProcessStartInfo(executable) { WorkingDirectory = repository, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--no-pager"); foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); await Task.WhenAll(output, error);
        if (process.ExitCode != 0) throw new InvalidOperationException(await error);
        return (await output).Replace("\r\n", "\n");
    }
}
