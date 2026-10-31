using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using GitCompass.Application;
using GitCompass.Domain;
using GitCompass.Infrastructure.Git;
using GitCompass.Infrastructure.GitHub;
using GitCompass.Infrastructure.Local;
using GitCompass.Terminal.Windows;
using Microsoft.Win32;

namespace GitCompass.Desktop;

public partial class ShellWindow : Window
{
    private readonly ShellViewModel model = new();
    private readonly AppStateStore store;
    private readonly WindowsBackdrop backdrop;
    private readonly HttpClient http = new();
    private readonly WindowsTokenStore tokens;
    private readonly List<OperationEvent> events = [];
    private readonly Dictionary<Guid, (Expander Expander, TextBox Text)> outputGroups = [];
    private readonly Dictionary<Guid, GitHubClient> githubClients = [];
    private readonly Dictionary<Guid, string> stashes = [];
    private sealed record UploadedVersion(string CommitId, string Url, string DestinationBranch, DateTimeOffset Time);
    private readonly Dictionary<Guid, UploadedVersion> uploadedVersions = [];
    private readonly List<(TerminalSession Session, TerminalView View, TabItem Tab)> sessions = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(8) };
    private AppSettings settings = new();
    /// <summary>Client ID embedded at build time (AssemblyMetadata GitCompassGitHubClientId), or null when none was shipped.</summary>
    internal static readonly string? ShippedGitHubClientId = GitHubClientIdSource.Normalize(typeof(ShellWindow).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false).OfType<System.Reflection.AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "GitCompassGitHubClientId")?.Value);
    private string? EffectiveGitHubClientId => GitHubClientIdSource.Resolve(settings.GitHubClientId, ShippedGitHubClientId);
    private GitWorkspace? workspace;
    private RemoteCheckScheduler? scheduler;
    private StructuredLogStore? logStore;
    private readonly DiagnosticExporter diagnosticExporter = new();
    private LearningCatalog? learning;
    private GitCommandInventoryResult? inventory;
    private bool ready, updating, reviewing, closing, closeCleanupComplete, tickRunning;
    private int viewClearAt;
    private int historySkip;
    private int renderGeneration;
    private string? historyLeftId, historyRightId, historyLeftLabel, historyRightLabel;
    private string historyFilter = "";
    private bool outputDirty, outputTruncated;
    private readonly DispatcherTimer outputTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private CancellationTokenSource? activeCancellation;
    private CancellationTokenSource? authenticationCancellation;
    private string? setupError;
    private string? damagedSettings;
    private string? pendingProjectFolder;
    private string appliedTheme = "System";
    private bool activityDockOpen;
    public string SmokeDescription => $"GitCompass WPF shell rendered. Page: {model.Page}. Project: {model.Snapshot?.Project.DisplayName ?? "none"}. Real observed branch: {model.Snapshot?.Branch ?? "none"}. Files: {model.Snapshot?.Files.Length.ToString() ?? "none"}. History rows: {model.History?.Commits.Length.ToString() ?? "none"}. Access: {(model.Snapshot is null ? "no project" : workspace?.Access.Get(model.Snapshot.ProjectId).ToString())}.";

    public ShellWindow(string? stateRoot = null)
    {
        store = new(stateRoot); tokens = new(store.Root);
        InitializeComponent(); backdrop = new(this); InitializeResponsiveLayout(); DataContext = model;
        AccessSelector.SelectedIndex = 0;
        OutputChannel.Items.Add("Current project activity"); OutputChannel.Items.Add("Git operations"); OutputChannel.Items.Add("Remote checks"); OutputChannel.Items.Add("All registered projects"); OutputChannel.Items.Add("GitHub activity"); OutputChannel.SelectedIndex = 0;
        timer.Tick += async (_, _) => await TickAsync();
        outputTimer.Tick += (_, _) => { if (outputDirty) { outputDirty = false; RenderOutput(); } }; outputTimer.Start();
        Activated += async (_, _) => { if (ready && !model.Busy && !reviewing) await RefreshAsync(false); };
        Closing += OnClosing;
        SystemEvents.UserPreferenceChanged += WindowsAppearanceChanged;
    }
    public void ConfigureView(string? page, string? theme)
    {
        if (theme is not null) ApplyTheme(theme);
        if (page is not null) Navigate(page);
    }

    public async Task InitializeAsync()
    {
        try { settings = await store.LoadSettingsAsync(); } catch (Exception e) { setupError = "Settings could not be loaded. " + e.Message; if (e is AppStateException state) damagedSettings = state.PreservedPath; }
        await InitializeBeginnerAsync();
        ApplyTheme(settings.Theme);
        try { learning = new LearningCatalog(); } catch (Exception e) { setupError = "Offline lessons could not be loaded. " + e.Message; }
        var executable = settings.GitExecutablePath ?? DependencyProbe.FindGitExecutable();
        if (executable is not null && File.Exists(executable))
        {
            workspace = GitWorkspaceFactory.Create(executable);
            if (learning is not null) inventory = await GitCommandInventory.ReadAsync(executable, learning.Manifest.SupportedCoreCommands);
            logStore = new(store.Root);
            workspace.Activity += ev => Dispatcher.BeginInvoke(() => AddEvent(ev));
            workspace.Access.Changed += grant => Dispatcher.BeginInvoke(() => { if (model.Snapshot?.ProjectId == grant.ProjectId) UpdateContext(); });
            StartScheduler();
        }
        else setupError = "Git for Windows was not found. Install Git, or set its git.exe path in Settings, then check again. Git remembers versions of your files on this computer. Settings has installation guidance.";
        ready = true; await UpdateRecentProjectsAsync(); UpdateContext(); RenderPage(); timer.Start();
    }

    private async Task UpdateRecentProjectsAsync()
    {
        updating = true;
        try { ProjectSelector.ItemsSource = await store.LoadProjectsAsync(); }
        catch (Exception e) { ShowProblem("Recent projects", e.Message); }
        finally { updating = false; }
    }
    public async Task OpenProjectAsync(string folder)
    {
        if (workspace is null) { pendingProjectFolder = folder; Banner(setupError ?? "Install Git to continue with this folder. Your files have not been changed."); Navigate("Settings"); return; }
        await WorkAsync(async () =>
        {
            authenticationCancellation?.Cancel();
            model.Snapshot = await workspace.OpenAsync(folder);
            model.History = await workspace.HistoryAsync(model.Snapshot.ProjectId);
            StatusBanner.Visibility = Visibility.Collapsed; OpenAffectedProjectButton.Visibility = Visibility.Collapsed;
            historySkip = 0;
            historyLeftId = historyRightId = historyLeftLabel = historyRightLabel = null; historyFilter = "";
            await store.RememberProjectAsync(model.Snapshot.Project.SelectedFolder, model.Snapshot.Project.DisplayName);
            await UpdateRecentProjectsAsync();
            updating = true;
            foreach (var item in ProjectSelector.Items)
                if (item is RecentProject recent && string.Equals(recent.Folder, model.Snapshot.Project.SelectedFolder, StringComparison.OrdinalIgnoreCase)) ProjectSelector.SelectedItem = item;
            updating = false;
            reviewing = false; model.Page = "Overview"; SelectNavigation("Overview");
            await OnBeginnerProjectOpenedAsync();
            if (compactNavigation) SetNavigationDrawer(false);
            UpdateContext(); RenderPage();
            AddEvent(new(Guid.NewGuid(), model.Snapshot.ProjectId, DateTimeOffset.Now, OperationState.Succeeded, "inspection", "Opened and inspected in " + AccessText(workspace.Access.Get(model.Snapshot.ProjectId).Level) + ". Repository inspection does not start a shell or fetch.", OperationKind.Inspect));
        });
    }
    private async void PickFolder(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Select your project folder", Multiselect = false };
        if (picker.ShowDialog(this) == true) await OpenProjectAsync(picker.FolderName);
    }
    private async void ProjectSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ready && !updating && ProjectSelector.SelectedItem is RecentProject recent) await OpenProjectAsync(recent.Folder);
    }
    private async void AccessChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || updating || workspace is null || model.Snapshot is null) return;
        var snapshot = model.Snapshot; var requested = (AccessLevel)Math.Max(0, AccessSelector.SelectedIndex); var old = workspace.Access.Get(snapshot.ProjectId);
        if (requested == old.Level && old.PendingLevel is null) return;
        if (requested != AccessLevel.ReadOnly && !snapshot.Project.Trusted)
        {
            var explanation = requested == AccessLevel.MonitorOnly
                ? "GitCompass will check the online copy for new versions. It won't change your files."
                : "GitCompass will be able to save versions, bring in updates and upload your work in this folder. You still confirm each step before it runs.";
            if (MessageBox.Show(this, explanation + "\n\nOnly do this for folders you made yourself or got from someone you trust, because a project can include its own settings that run when Git works. Allow " + AccessText(requested) + " for this folder?", "Allow GitCompass to work in " + snapshot.Project.DisplayName, MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) { UpdateContext(); return; }
            workspace.SetTrusted(snapshot.ProjectId, true);
        }
        if (requested < old.Level && (sessions.Any(s => s.Session.ProjectId == snapshot.ProjectId.ToString() && !s.Session.IsClosed) || model.Busy))
        {
            if (MessageBox.Show(this, "Switching to a safer mode stops anything GitCompass is doing in this folder and closes its terminal windows. Your files are safe. Continue?", "Switch to a safer mode", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) { UpdateContext(); return; }
        }
        workspace.Access.SetLevel(snapshot.ProjectId, requested);
        if (requested == AccessLevel.ReadOnly) workspace.SetMonitoring(snapshot.ProjectId, false);
        var accessGeneration = workspace.Access.Get(snapshot.ProjectId).Generation;
        await RefreshAsync(false);
        // An older access-change refresh must not rebuild a newer project's controls or review.
        if (model.Snapshot?.ProjectId != snapshot.ProjectId || workspace.Access.Get(snapshot.ProjectId).Generation != accessGeneration) return;
        UpdateContext(); if (!reviewing) RenderPage();
    }
    private void UpdateContext()
    {
        if (AccessSelector is null) return;
        updating = true;
        var snapshot = model.Snapshot;
        HeaderContext.Visibility = HeaderStatus.Visibility = snapshot is null || model.Page == "Liquid Glass preview" ? Visibility.Collapsed : Visibility.Visible;
        ProjectPlaceholder.Visibility = snapshot is null ? Visibility.Visible : Visibility.Collapsed;
        if (snapshot is null || workspace is null)
        {
            AccessSelector.IsEnabled = false; CheckRemoteButton.IsEnabled = false; NewTerminalButton.IsEnabled = false; BranchLabel.Text = "No project"; ProjectPath.Text = "Select a project folder to begin."; FreshnessLabel.Text = "Remote not checked";
            AccessHelper.Text = "Opening a folder starts with local inspection in Read-only.";
        }
        else
        {
            var grant = workspace.Access.Get(snapshot.ProjectId);
            AccessSelector.IsEnabled = true; AccessSelector.SelectedIndex = (int)grant.Level;
            var hasWorkingRepository = snapshot.Project.Kind is RepositoryKind.WorkingTree or RepositoryKind.LinkedWorktree;
            BranchLabel.Text = snapshot.Project.Kind == RepositoryKind.Candidate ? "Not saving versions yet" : snapshot.BranchState == BranchState.Detached ? "Viewing an old version (detached HEAD)" : snapshot.Branch ?? "No saved versions yet";
            ProjectPath.Text = snapshot.Project.RootPath + (snapshot.Project.SelectedFolder == snapshot.Project.RootPath ? "" : "  • selected " + snapshot.Project.SelectedFolder);
            ProjectPath.ToolTip = ProjectPath.Text;
            var last = snapshot.Remote.LastSuccessfulCheck;
            FreshnessLabel.Text = snapshot.Project.Kind == RepositoryKind.Candidate ? "Git not set up • no remote checks" : last.HasValue ? "Last check " + last.Value.ToLocalTime().ToString("g") + (snapshot.Remote.State == RemoteCheckState.Succeeded ? "" : " • stale") : "Remote not checked • cached refs";
            CheckRemoteButton.IsEnabled = hasWorkingRepository && snapshot.HasRemotes && snapshot.Upstream is not null && grant.Level >= AccessLevel.MonitorOnly && grant.PendingLevel is null && !model.Busy;
            CheckRemoteButton.ToolTip = !hasWorkingRepository || !snapshot.HasRemotes ? "This project needs an online or team connection before checking for updates." : snapshot.Upstream is null ? "Choose the connected team branch in project setup before checking its updates." : grant.Level == AccessLevel.ReadOnly ? "Choose Monitor only to check for team updates. Read-only uses the last known information." : "Checks the team's history without changing your project files.";
            ToolTipService.SetShowOnDisabled(CheckRemoteButton, true);
            NewTerminalButton.IsEnabled = !model.Busy && Permission(snapshot.ProjectId, OperationKind.Terminal);
            NewTerminalButton.ToolTip = grant.Level < AccessLevel.AllowChanges ? "Requires Allow changes: a real shell can modify files. Use Branches or History graph to inspect branches safely." : "An interactive shell can modify files. An active operation or access transition may need to finish first.";
            ToolTipService.SetShowOnDisabled(NewTerminalButton, true);
            TerminalModeText.Text = grant.PendingLevel is not null ? "Switching mode: closing open terminal windows first." : grant.Level == AccessLevel.AllowChanges ? "Each terminal stays linked to the project it was opened in." : "To type commands here, first choose Allow changes at the top.";
            AccessHelper.Text = grant.PendingLevel is not null ? "Switching to the safer mode as soon as running steps stop." : grant.Level switch
            {
                AccessLevel.ReadOnly => "Read-only: look around without changing the project. Choose Allow changes when you are ready to save a version.",
                AccessLevel.MonitorOnly => "Monitor only: look around and check for team updates. Saving versions or applying updates requires Allow changes.",
                _ => "Allow changes: you can review and save versions, apply updates, and upload saved work for this session."
            };
            if (snapshot.Project.Kind == RepositoryKind.Candidate && grant.PendingLevel is null)
                AccessHelper.Text = "Your files are unchanged. Home helps you choose local version history or your team's existing project.";
            if (!string.Equals(snapshot.Project.SelectedFolder, snapshot.Project.RootPath, StringComparison.OrdinalIgnoreCase))
                AccessHelper.Text += " You selected a folder inside this project; Git tracks the whole project shown above.";
            if (grant.PendingLevel is not null) Banner("Access transition pending: closing owned activity before " + AccessText(grant.PendingLevel.Value) + " becomes active.");
        }
        updating = false; RenderOutput();
        UpdateResponsiveLayout();
    }
    private async Task RefreshAsync(bool render = true)
    {
        if (workspace is null || model.Snapshot is null || model.Busy) return;
        var id = model.Snapshot.ProjectId;
        try
        {
            var snapshot = await workspace.RefreshAsync(id);
            if (model.Snapshot?.ProjectId != id) return;
            var changed = snapshot.Fingerprint != model.Snapshot.Fingerprint;
            model.Snapshot = snapshot;
            if (changed || model.History is null)
            {
                try { model.History = historyLeftId is null ? await workspace.HistoryAsync(id) : await workspace.HistoryAsync(id, new HistoryRequest(historyLeftId, historyRightId)); }
                catch (InvalidOperationException) when (historyLeftId is not null) { historyLeftId = historyRightId = historyLeftLabel = historyRightLabel = null; model.History = await workspace.HistoryAsync(id); Banner("A selected history reference changed. The current-branch view was restored; choose the branches again to compare them."); }
                historySkip = 0;
            }
            UpdateContext(); if (render && !reviewing) RenderPage();
        }
        catch (Exception e) { ShowProblem("Inspection", e.Message); }
    }
    private async Task TickAsync()
    {
        if (!ready || workspace is null || tickRunning || model.Busy || closing) return;
        tickRunning = true;
        try
        {
            if (!reviewing) await RefreshAsync(model.Page == "Overview");
        }
        finally { tickRunning = false; }
    }
    private void StartScheduler()
    {
        if (workspace is null) return;
        scheduler = new(workspace, TimeSpan.FromMinutes(Math.Clamp(settings.FetchIntervalMinutes, 1, 1440)), time => MonitoringNotificationPolicy.ShouldNotify(settings, time));
        scheduler.Notice += notice => Dispatcher.BeginInvoke(() =>
        {
            var project = workspace.Projects.FirstOrDefault(p => p.Id == notice.ProjectId);
            Banner((project?.DisplayName ?? "Project") + ": " + notice.Message);
            OpenAffectedProjectButton.Tag = notice.ProjectId; OpenAffectedProjectButton.Visibility = Visibility.Visible;
            AutomationProperties.SetName(OpenAffectedProjectButton, "Open affected project " + (project?.DisplayName ?? "project") + " to review the notice");
        });
        scheduler.Fault += error => Dispatcher.BeginInvoke(() => ShowProblem("Remote monitoring", error.Message)); scheduler.Start();
    }
    private async void CheckRemote(object sender, RoutedEventArgs e) => await RequestAsync(OperationKind.Fetch);
    private async Task WorkAsync(Func<Task> work)
    {
        if (model.Busy) { Banner("Please wait: GitCompass is still finishing the last step. You can watch it under Activity details."); return; }
        model.Busy = true; activeCancellation = new(); UpdateContext();
        try { await work(); }
        catch (OperationCanceledException) { Banner("Stopped. GitCompass is checking your project again so the screen shows what really happened."); }
        catch (Exception e) { ShowProblem("That step didn't work", e.Message); }
        finally { model.Busy = false; activeCancellation?.Dispose(); activeCancellation = null; UpdateContext(); }
    }
    private async Task RequestAsync(OperationKind kind, ImmutableArray<string> paths = default, string? message = null, string? branch = null, string? target = null, string? remote = null, string? remoteUrl = null, ImmutableArray<string> ignore = default, bool includeUntracked = false, bool setUpstream = false, string? authorName = null, string? authorEmail = null)
    {
        if (workspace is null || model.Snapshot is null) return;
        await WorkAsync(async () =>
        {
            var request = new OperationRequest(model.Snapshot.ProjectId, kind, paths, message, remote, remoteUrl, branch, target, ignore, includeUntracked, setUpstream, authorName, authorEmail);
            var preview = await workspace.PreviewAsync(request, activeCancellation!.Token);
            model.Snapshot = workspace.GetSnapshot(request.ProjectId); ShowPreview(preview);
        });
    }
    private void ShowPreview(OperationPreview preview)
    {
        reviewing = true; PageContent.Children.Clear();
        Heading("Review: " + preview.Title, "Confirm the project, branch, destination, and effects before running this operation.");
        AddCard(PageContent, Redactor.Redact(preview.Explanation));
        AddCard(PageContent, "Project: " + model.Snapshot!.Project.DisplayName + "\nBranch: " + (model.Snapshot.Branch ?? model.Snapshot.BranchState.ToString()) + "\nAccess: " + AccessText(preview.AccessLevel) + "\nTargets: " + string.Join("\n", preview.Targets.Select(Redactor.Redact)));
        PageContent.Children.Add(Mono(Redactor.Redact(preview.CommandDisplay), 110));
        Text(PageContent, "This preview matches your project exactly as it is now. If files change, or you turn off Allow changes, GitCompass will ask you to review again.", true);
        var actions = Row();
        actions.Children.Add(Action("Confirm and run this step", async () =>
        {
            await WorkAsync(async () =>
            {
                var result = await workspace!.ExecuteAsync(preview, activeCancellation!.Token);
                if (preview.Request.Kind == OperationKind.Fetch && result.State == OperationState.Succeeded) scheduler?.Resume(result.Snapshot.ProjectId);
                if (preview.Request.Kind == OperationKind.Push && result.State == OperationState.Succeeded && preview.ReviewedHeadId is not null && preview.Request.RemoteUrl is not null && preview.Request.Branch is not null)
                    uploadedVersions[result.Snapshot.ProjectId] = new(preview.ReviewedHeadId, preview.Request.RemoteUrl, preview.Request.Branch, DateTimeOffset.Now);
                model.Snapshot = result.Snapshot; model.History = await workspace.HistoryAsync(result.Snapshot.ProjectId);
                historySkip = 0; historyLeftId = historyRightId = historyLeftLabel = historyRightLabel = null;
                if (result.PreservedStashId is not null) stashes[result.Snapshot.ProjectId] = result.PreservedStashId;
                reviewing = false; RememberUndoable(preview, result);
                var localSaveNote = preview.Request.Kind == OperationKind.Commit && result.State == OperationState.Succeeded
                    ? " This version is saved on this computer. It has not uploaded your work to a team service."
                    : "";
                Banner(result.Summary + localSaveNote + (result.OutputTruncated ? " Output was truncated; review retained diagnostics." : ""));
                await OnBeginnerOperationCompletedAsync(preview.Request.Kind, result.State); RenderPage();
                if (result.State != OperationState.Succeeded) ShowProblem(preview.Title, result.Summary);
            });
        }, true));
        actions.Children.Add(Action("Cancel review", () => { reviewing = false; RenderPage(); }));
        actions.Children.Add(Action("Cancel running operation", () => activeCancellation?.Cancel()));
        PageContent.Children.Add(actions);
    }
    private void NavigationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || updating || Navigation.SelectedItem is not ListBoxItem item) return;
        reviewing = false; model.Page = item.Tag?.ToString() ?? "Overview"; RenderPage();
    }
    private void Navigate(string page) { reviewing = false; model.Page = page; SelectNavigation(page); RenderPage(); }
    private void SelectNavigation(string page)
    {
        var category = page switch { "Walkthrough" => "Learn", "Author setup" => "Changes", "Liquid Glass preview" => "Settings", _ => page };
        updating = true; foreach (ListBoxItem item in Navigation.Items) if (item.Tag?.ToString() == category) Navigation.SelectedItem = item; updating = false;
    }
    private static string PageName(string page) => page switch
    {
        "Overview" => "Home", "Changes" => "Changed files", "History graph" => "Saved versions",
        "Pull requests" => "Sharing", "Walkthrough" => "First version walkthrough", "Author setup" => "Author details", _ => page
    };
    private void NavigationSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (Navigation is null) return;
        var query = NavigationSearch.Text.Trim(); var matches = 0;
        var ignoredWords = new[] { "my", "the", "a", "an", "to", "how", "i", "do", "can", "what", "is" };
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => !ignoredWords.Contains(word, StringComparer.OrdinalIgnoreCase)).ToArray();
        foreach (ListBoxItem item in Navigation.Items)
        {
            var route = item.Tag?.ToString() ?? "";
            var terms = route switch
            {
                "Overview" => "home status overview next step project",
                "Projects" => "projects folder open choose download clone setup",
                "Changes" => "changed files save work commit select stage diff",
                "History graph" => "saved versions history graph log timeline",
                "Branches" => "branches separate task switch",
                "Pull requests" => "sharing github online upload push team review pull request",
                "Learn" => "learn git help explain explanation walkthrough tutorial practice commit stage branch branches push pull github save upload",
                _ => "settings appearance theme light dark system notifications installation"
            };
            var visible = query.Length == 0 || words.Length > 0 && words.All(word => terms.Contains(word, StringComparison.OrdinalIgnoreCase));
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed; if (visible) matches++;
        }
        if (NavigationSearchSummary is not null)
        {
            NavigationSearchSummary.Text = matches == 0 ? "No pages match. Try save, history, sharing, or theme." : "";
            NavigationSearchSummary.Visibility = matches == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    private void ToggleActivityDock(object sender, RoutedEventArgs e) => SetActivityDock(!activityDockOpen);
    private void SetActivityDock(bool open)
    {
        activityDockOpen = open;
        UpdateResponsiveLayout();
        ActivityToggleButton.Content = open ? "Hide activity details" : "Show activity details";
        AutomationProperties.SetName(ActivityToggleButton, ActivityToggleButton.Content.ToString());
    }
    private void BuildProjects()
    {
        Heading("Projects", "Pick the folder where your project files live. GitCompass will check if it already keeps saved versions (Git history).");
        PageContent.Children.Add(Action("Select project folder", () => PickFolder(this, new RoutedEventArgs()), true));
        Text(PageContent, "Opening a folder does not change its files or start online sharing.", true);
        foreach (var recent in ProjectSelector.Items.OfType<RecentProject>())
            PageContent.Children.Add(SettingsRow(recent.DisplayName, recent.Folder, Action("Open project", async () => await OpenProjectAsync(recent.Folder))));
        if (ProjectSelector.Items.Count == 0) Text(PageContent, "No projects yet. Click \"Select project folder\" above to start. New to this? Open Learn on the left, or create a practice folder there.", true);
        BuildGuideEntry(PageContent);
    }
    private void RenderPage()
    {
        HookWhatsThis();
        RenderPageContent();
        ApplyGlossary(PageContent);
    }
    /// <summary>Hover glossary: any text that mentions Git words gets a tooltip with everyday definitions (and the lesson title from the offline catalog).</summary>
    private void ApplyGlossary(DependencyObject? root)
    {
        if (root is null) return;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is TextBlock { ToolTip: null } block && GitGlossary.TermsIn(block.Text) is { Count: > 0 } terms)
            {
                var lines = terms.Take(3).Select(t => t.Term + ": " + t.Plain + (learning?.Find(t.LessonId) is { } lesson ? "  (Lesson: " + lesson.Title + ")" : ""));
                block.ToolTip = string.Join("\n", lines) + "\nPress F1 for more, with links to the official docs.";
                ToolTipService.SetInitialShowDelay(block, 400);
                System.Windows.Automation.AutomationProperties.SetHelpText(block, string.Join(" ", terms.Take(3).Select(t => t.Term + ": " + t.Plain)));
            }
            ApplyGlossary(child);
        }
    }
    private void RenderPageContent()
    {
        if (!ready || PageContent is null) return;
        renderGeneration++;
        PageContent.Children.Clear();
        HeaderContext.Visibility = HeaderStatus.Visibility = model.Snapshot is null || model.Page == "Liquid Glass preview" ? Visibility.Collapsed : Visibility.Visible;
        if (model.Page == "Liquid Glass preview") { BuildLiquidGlassPreview(); return; }
        if (model.Page == "Projects") { BuildProjects(); return; }
        if (model.Page == "Walkthrough") { BuildWalkthrough(); return; }
        if (model.Page == "Author setup" && model.Snapshot is not null && workspace is not null) { BuildAuthorSetup(); return; }
        if (model.Page == "Learn") { BuildLearn(); return; }
        if (model.Page == "Settings") { BuildSettings(); return; }
        if (model.Snapshot is null || workspace is null) { BuildWelcome(); return; }
        if (model.Snapshot.Project.Kind == RepositoryKind.Candidate && model.Page != "Overview") { BuildNonGitPage(); return; }
        switch (model.Page) { case "Changes": BuildChanges(); break; case "History graph": BuildHistory(); break; case "Branches": BuildBranches(); break; case "Pull requests": BuildPullRequests(); break; default: BuildOverview(); break; }
    }
    private void BuildWelcome()
    {
        BuildBeginnerWelcome();
    }
    private void BuildOverview()
    {
        var snapshot = model.Snapshot!;
        if (snapshot.Project.Kind == RepositoryKind.Candidate) { BuildCandidateOverview(); return; }
        var plan = workspace!.Plan(snapshot.ProjectId, model.Goal);
        var alreadyUploaded = UploadedCurrentVersion(snapshot);
        var firstIsUploadedPush = alreadyUploaded && plan.Steps.FirstOrDefault()?.Action == OperationKind.Push;
        Heading(firstIsUploadedPush ? "Your reviewed version was uploaded" : plan.Steps.FirstOrDefault()?.Title ?? "Understand this project", firstIsUploadedPush ? "Check remote to refresh the cached team copy. A successful upload does not refresh these cached comparisons." : plan.Steps.FirstOrDefault()?.Reason ?? "Repository status was read from Git.");
        BuildGuideEntry(PageContent);
        if (snapshot.HasLocalEdits) PageContent.Children.Add(Action("See your changed files", () => Navigate("Changes"), true));
        var goal = new ComboBox { Width = 250, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = new[] { "Understand this project", "Upload my work", "Get the latest changes", "Start a task", "Prepare a pull request", "Understand this error" }, SelectedIndex = (int)model.Goal, Margin = new(0, 0, 0, 15) };
        AutomationProperties.SetName(goal, "Your current goal"); goal.SelectionChanged += (_, _) => { model.Goal = (UserGoal)goal.SelectedIndex; BuildOverviewReplacement(); }; PageContent.Children.Add(goal);
        var stats = Columns(3);
        AddCard(stats, Stat(snapshot.Files.Length.ToString(), "changed files", "Staged and unstaged contents are reviewed separately.", () => Navigate("Changes")), 0);
        AddCard(stats, Stat(!snapshot.HasRemotes ? "Local" : snapshot.Behind?.ToString() ?? "?", "team updates", !snapshot.HasRemotes ? "No online connection. Local version history works on its own." : snapshot.Upstream is null ? "Choose a team branch before comparing." : "Versions in the last checked team history; review before applying.", () => { if (snapshot.HasRemotes) Navigate("History graph"); else BuildProjectSetup(); }), 1);
        AddCard(stats, Stat(!snapshot.HasRemotes ? "Here" : snapshot.Ahead?.ToString() ?? "?", "versions saved here", !snapshot.HasRemotes ? "Saved versions stay on this computer. Sharing is optional." : snapshot.Upstream is null ? "Upload status is unknown until a connection is configured." : "Versions ahead of the last checked team copy. Upload is reviewed separately.", () => Navigate("History graph")), 2); PageContent.Children.Add(stats);
        if (uploadedVersions.TryGetValue(snapshot.ProjectId, out var uploaded))
            AddCard(PageContent, "Last successful upload in this session: version " + Short(uploaded.CommitId) + " to " + Redactor.Redact(uploaded.Url) + " • branch " + uploaded.DestinationBranch + " at " + uploaded.Time.ToLocalTime().ToString("g") + ".\nCached team comparisons can still show local-only commits until another Check remote. This records the upload at that time; it does not claim the server is unchanged now.");
        var body = Columns(2);
        var next = new StackPanel(); Subheading(next, "Next steps");
        var number = 1;
        foreach (var step in plan.Steps)
        {
            if (alreadyUploaded && step.Action == OperationKind.Push)
            {
                Text(next, number++ + "  Refresh the cached team copy", false, 16, FontWeights.SemiBold);
                Text(next, "This exact version and upload destination succeeded earlier in this session. Check remote before judging the cached counts or reviewing another upload.", true);
                next.Children.Add(OperationButton("Preview checking for updates (fetch)", OperationKind.Fetch, async () => await RequestAsync(OperationKind.Fetch)));
                continue;
            }
            Text(next, number++ + "  " + step.Title, false, 16, FontWeights.SemiBold); Text(next, step.Reason, true);
            var details = new StackPanel(); Text(details, "Effect: " + step.Effects); Text(details, "Before you start: " + step.Prerequisites); Text(details, "If this fails: " + step.Recovery); details.Children.Add(Action("Learn more in a short lesson", () => ShowLesson(step.Lesson)));
            next.Children.Add(new Expander { Header = "Explain this step", Content = details });
            if (step.Action is OperationKind.Stage or OperationKind.Commit or OperationKind.Unstage) next.Children.Add(Action("Go to changed files", () => Navigate("Changes")));
            else if (step.Action is not null) next.Children.Add(OperationButton(step.Title, step.Action.Value, () => RecommendAction(step)));
            else next.Children.Add(Action("See changes and details", () => Navigate(snapshot.HasLocalEdits ? "Changes" : "History graph")));
        }
        AddCard(body, next, 0);
        var graph = new StackPanel(); Subheading(graph, "Current history");
        if (model.History?.Commits.Length > 0) graph.Children.Add(HistoryControl(model.History.Commits.Take(7).ToImmutableArray(), false)); else Text(graph, "No saved versions (commits) yet. Save your first version on the Changes page.", true);
        graph.Children.Add(Action("See all saved versions (history)", () => Navigate("History graph"))); AddCard(body, graph, 1); PageContent.Children.Add(body);
        AddCard(PageContent, "Quick guide: Check for updates (fetch) looks at the online copy without changing your files. Get updates (pull or merge) brings new saved versions into your work. Save a version (commit) keeps a snapshot on this computer. Upload (push) sends your saved versions online.");
        var row = Row(); row.Children.Add(Action("See your changes", () => Navigate("Changes"), true)); row.Children.Add(Action("Check again for changes", async () => await RefreshAsync())); row.Children.Add(Action("Your name and online copy (setup)", BuildProjectSetup)); PageContent.Children.Add(row);
        if (snapshot.ActiveOperation != GitOperation.None) BuildRecovery(PageContent);
    }
    private bool UploadedCurrentVersion(RepositorySnapshot snapshot) => snapshot.Push is { } push && uploadedVersions.TryGetValue(snapshot.ProjectId, out var uploaded) && uploaded.CommitId == snapshot.HeadId && uploaded.Url == push.Url && "refs/heads/" + uploaded.DestinationBranch == push.DestinationRef;
    private void BuildOverviewReplacement() { if (model.Page == "Overview" && !reviewing) { PageContent.Children.Clear(); BuildOverview(); } }
    private void RecommendAction(Recommendation step)
    {
        switch (step.Action)
        {
            case OperationKind.Commit: case OperationKind.Stage: case OperationKind.Unstage: Navigate("Changes"); break;
            case OperationKind.Initialize: BuildFirstProject(); break;
            case OperationKind.ConfigureIdentity: Navigate("Author setup"); break;
            case OperationKind.ConfigureRemote: BuildProjectSetup(); break;
            case OperationKind.Merge: BuildMerge(); break;
            case OperationKind.Stash: BuildStash(); break;
            case OperationKind.CreateBranch: case OperationKind.SwitchBranch: Navigate("Branches"); break;
            default: _ = RequestAsync(step.Action!.Value); break;
        }
    }
    private void BuildFirstProject()
    {
        PageContent.Children.Clear(); BuildCandidateOverview();
    }
    private void BuildNonGitPage()
    {
        Heading("This folder doesn't keep saved versions yet", "Your files are safe and unchanged. GitCompass has not turned on version saving (Git) here.");
        var explanation = model.Page switch
        {
            "Changes" => "Git cannot label files as staged, unstaged or changed against a recorded version until this folder has Git history. Your files are still in the folder.",
            "History graph" => "There are no Git commits to display in this folder. Ordinary files and ZIP copies do not contain a Git history graph.",
            "Branches" => "There are no Git branches in this folder. A branch names recorded Git work; the first local version starts that history.",
            _ => "Pull requests need a hosted Git repository and a published source branch. This folder has not been connected to Git yet."
        };
        AddCard(PageContent, explanation);
        Text(PageContent, "You can keep this as an ordinary folder. When ready, review starting local history in this folder, or clone your team's existing repository into a separate empty folder. GitHub sign-in is optional for local Git work.", true);
        var actions = Row();
        actions.Children.Add(Action("Start saving versions here", () => Navigate("Overview"), true));
        actions.Children.Add(Action("Learn how saving and uploading work", () => ShowLesson("journey")));
        actions.Children.Add(Action("Choose another folder", () => PickFolder(this, new RoutedEventArgs())));
        PageContent.Children.Add(actions);
    }
    public const string GitHubSignInLabel = "Sign in to GitHub";
    public const string UploadSignInWarning = "When you upload for the first time, a GitHub sign-in window from Git will open. That's expected: choose \"Sign in with your browser\" and approve it on github.com.";
    /// <summary>Three plain steps to get an empty GitHub repository and its web address (Mission 5 and Project setup).</summary>
    private void BuildGitHubSteps(Panel target)
    {
        Subheading(target, "Put your project on GitHub in 3 steps");
        Text(target, "1. Make a free GitHub account (skip if you have one).", true);
        target.Children.Add(DocChip("https://github.com/signup"));
        Text(target, "2. Create a new, empty repository (an online project folder). Give it a name, and don't tick \"Add a README\".", true);
        target.Children.Add(DocChip("https://github.com/new"));
        Text(target, "3. Copy its web address (it looks like https://github.com/your-name/your-project) and paste it below.", true);
    }
    private void BuildProjectSetup()
    {
        PageContent.Children.Clear(); reviewing = true; Heading("Project setup", "Add your name for saved versions, and connect an online copy on GitHub when you're ready.");
        var name = Input(PageContent, "Your name (shown on each saved version)", model.Snapshot?.AuthorName ?? ""); var email = Input(PageContent, "Your email (shown on each saved version)", model.Snapshot?.AuthorEmail ?? "");
        PageContent.Children.Add(OperationButton("Preview saving your name and email", OperationKind.ConfigureIdentity, async () => await RequestAsync(OperationKind.ConfigureIdentity, authorName: name.Text, authorEmail: email.Text)));
        Text(PageContent, "This name and email label your saved versions. It is not the same as signing in to GitHub.", true);
        var github = new StackPanel(); BuildGitHubSteps(github); AddCard(PageContent, github);
        var url = Input(PageContent, "Web address of your GitHub repository", "");
        var current = model.Snapshot?.Branch ?? "main";
        var advanced = new StackPanel();
        var remote = Input(advanced, "Short name for the online copy (remote)", "origin");
        var publishBranch = Input(advanced, "Line of work to upload to (branch)", current);
        PageContent.Children.Add(new Expander { Header = "Advanced (most people can skip this)", Content = advanced });
        PageContent.Children.Add(OperationButton("Preview connecting the online copy (remote)", OperationKind.ConfigureRemote, async () =>
        {
            if (string.IsNullOrWhiteSpace(url.Text)) { Banner("Paste your GitHub repository's web address first (step 3)."); return; }
            await RequestAsync(OperationKind.ConfigureRemote, remote: remote.Text, remoteUrl: url.Text.Trim(), branch: null, setUpstream: false);
        }));
        Subheading(PageContent, "Upload your saved versions");
        Text(PageContent, "This copies your saved versions to GitHub. Changes you haven't saved yet stay on your computer only.", true);
        Text(PageContent, UploadSignInWarning, true);
        PageContent.Children.Add(OperationButton("Preview your first upload (push)", OperationKind.Push, async () => await RequestAsync(OperationKind.Push, remote: remote.Text, branch: publishBranch.Text, setUpstream: true)));
        PageContent.Children.Add(Action("Back to Overview", () => Navigate("Overview")));
    }
    /// <summary>The most recent locally reversible step: a stage (undone by unstaging the same paths) or an unpublished commit (undone by soft reset). Both go through a normal preview.</summary>
    private sealed record UndoableStep(Guid ProjectId, OperationKind Kind, ImmutableArray<string> Paths, string? CommitId);
    private UndoableStep? lastUndoable;
    private void RememberUndoable(OperationPreview preview, OperationResult result)
    {
        if (result.State != OperationState.Succeeded) { return; }
        lastUndoable = preview.Request.Kind switch
        {
            OperationKind.Stage => new(result.Snapshot.ProjectId, OperationKind.Stage, preview.Request.Paths, result.Snapshot.HeadId),
            OperationKind.Commit when result.Snapshot.HeadId is { } head => new(result.Snapshot.ProjectId, OperationKind.Commit, [], head),
            _ => null, // any other step (including an undo or a push) clears the offer
        };
    }
    private void BuildUndoCard(RepositorySnapshot snapshot)
    {
        if (lastUndoable is not { } undo || undo.ProjectId != snapshot.ProjectId || undo.CommitId != snapshot.HeadId) return;
        var card = new StackPanel(); Subheading(card, "Undo last step");
        if (undo.Kind == OperationKind.Stage)
        {
            var paths = undo.Paths.Where(p => snapshot.Files.Any(f => f.Path == p && f.IsStaged)).ToImmutableArray();
            if (paths.Length == 0) return;
            Text(card, "You just staged " + string.Join(", ", paths) + ". Undoing takes them out of the next save. Your files aren't changed.", true);
            card.Children.Add(OperationButton("Preview undo: take files out (unstage)", OperationKind.Unstage, async () => await RequestAsync(OperationKind.Unstage, paths)));
        }
        else
        {
            Text(card, "You just saved a version on this computer. Undoing removes that save but keeps every change staged, so you can fix the message or contents and save again. This is offered only while the version hasn't been uploaded.", true);
            card.Children.Add(OperationButton("Preview undo: remove last saved version", OperationKind.UndoCommit, async () => await RequestAsync(OperationKind.UndoCommit, target: undo.CommitId)));
        }
        AddCard(PageContent, card);
    }
    private void BuildChanges()
    {
        var snapshot = model.Snapshot!;
        Heading("Changed files", "Tick the files you want in your next saved version (commit). Saving keeps it on this computer; uploading is a separate step.");
        BuildGuideEntry(PageContent);
        BuildUndoCard(snapshot);
        if (!snapshot.HasAuthorIdentity)
            PageContent.Children.Add(SettingsRow("Set the author for your versions", "Git needs a name and email before your first save. These label your versions; they are separate from signing in.", Action("Set author details", () => Navigate("Author setup"))));
        if (workspace!.Access.Get(snapshot.ProjectId).Level != AccessLevel.AllowChanges)
            PageContent.Children.Add(SettingsRow("Enable saving for this session", "Choose Allow changes in Project access above. Each guided operation still asks you to review its effects.", Action("Review project access", () => { AccessSelector.Focus(); AccessSelector.IsDropDownOpen = true; })));
        var commitPanel = new StackPanel();
        var row = Row(); row.Children.Add(Action("Check again for changes", async () => await RefreshAsync())); row.Children.Add(Action("Choose files to never save (ignore)", BuildIgnore)); row.Children.Add(Action(GitGlossary.Label("stash") + " for later", BuildStash)); row.Children.Add(Action("Describe this version", () => commitPanel.BringIntoView())); PageContent.Children.Add(row);
        var columns = Columns(2); var unstaged = FileGroup(snapshot.Files.Where(f => f.IsUnstaged).ToArray(), "Unstaged and new files", DiffKind.Worktree, OperationKind.Stage); var staged = FileGroup(snapshot.Files.Where(f => f.IsStaged).ToArray(), "Staged contents", DiffKind.Staged, OperationKind.Unstage); AddCard(columns, unstaged, 0); AddCard(columns, staged, 1); PageContent.Children.Add(columns);
        var allStaged = snapshot.Files.Where(f => f.IsStaged).ToArray();
        Subheading(commitPanel, "Save a version on this computer (commit)"); Text(commitPanel, allStaged.Length == 0 ? "Nothing is chosen yet. Tick files on the left and choose \"Preview adding chosen files (stage)\" first." : $"This save will include the {allStaged.Length} chosen file(s). Files you did not choose, and unsaved edits in your editor, are not included.", true);
        var message = Input(commitPanel, "Message: what did you change, and why? (commit message)", "", true);
        commitPanel.Children.Add(OperationButton("Preview saving this version (commit)", OperationKind.Commit, async () => await RequestAsync(OperationKind.Commit, message: message.Text))); PageContent.Children.Add(commitPanel);
        if (snapshot.ActiveOperation != GitOperation.None) BuildRecovery(PageContent);
    }
    private FrameworkElement FileGroup(FileState[] files, string title, DiffKind diffKind, OperationKind operation)
    {
        var panel = new StackPanel(); Subheading(panel, operation == OperationKind.Stage ? "Choose files for this version" : "Already chosen for the version (staged)");
        Text(panel, operation == OperationKind.Stage ? "Tick a file to see what changed, then add it to your next version (stage)." : "These will go into your next saved version. You can still take any of them out.", true, 12);
        var list = new ListBox { Height = files.Length == 0 ? 60 : 180, SelectionMode = SelectionMode.Extended };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        foreach (var file in files)
        {
            var item = new ListBoxItem { Tag = file, Padding = new(8) };
            var choice = new CheckBox { Content = new TextBlock { Text = FileDescription(file), TextWrapping = TextWrapping.Wrap }, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top };
            AutomationProperties.SetName(choice, FileDescription(file));
            choice.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(ListBoxItem.IsSelected)) { Source = item, Mode = BindingMode.TwoWay });
            item.Content = choice; list.Items.Add(item);
        }
        AutomationProperties.SetName(list, title); panel.Children.Add(list);
        if (files.Length == 0) Text(panel, operation == OperationKind.Stage ? "Nothing changed since your last saved version. Edit a file in your project, then choose \"Check again for changes\"." : "Nothing chosen yet. Tick files on the left to add them.", true);
        var diff = Mono("Click a file to see what changed (diff). Lines starting with + were added; lines starting with - were removed.", 180);
        var details = new Expander { Header = "See what changed in this file", Content = diff };
        list.SelectionChanged += async (_, _) =>
        {
            if (list.SelectedItem is not ListBoxItem { Tag: FileState file }) return;
            try { var result = await workspace!.DiffAsync(model.Snapshot!.ProjectId, new(diffKind, file.Path)); diff.Text = result.Text + (result.Truncated ? "\n[Diff truncated]" : "") + (result.Problem is null ? "" : "\n" + result.Problem); details.IsExpanded = true; if (result.Problem is null) await OnBeginnerDiffViewedAsync(file.Path); }
            catch (Exception e) { diff.Text = e.Message; }
        };
        panel.Children.Add(OperationButton(operation == OperationKind.Stage ? "Preview adding chosen files (stage)" : "Preview taking chosen files out (unstage)", operation, async () =>
        {
            var paths = list.SelectedItems.OfType<ListBoxItem>().Select(i => ((FileState)i.Tag).Path).ToImmutableArray();
            if (paths.Length == 0) { Banner("Tick at least one file first, then try again."); return; }
            await RequestAsync(operation, paths: paths);
        }));
        panel.Children.Add(details);
        return panel;
    }
    private async void BuildIgnore()
    {
        if (workspace is null || model.Snapshot is null) return;
        await WorkAsync(async () =>
        {
            var suggestions = await workspace.IgnoreSuggestionsAsync(model.Snapshot.ProjectId);
            reviewing = true; PageContent.Children.Clear(); Heading("Files to never save (ignore rules)", "Ignored files are skipped by Git. This only affects files Git isn't saving yet; files already saved stay in your history.");
            var choices = new List<(CheckBox Box, IgnoreSuggestion Suggestion)>();
            foreach (var suggestion in suggestions)
            {
                var box = new CheckBox { Content = suggestion.Pattern + " — " + suggestion.Reason, IsChecked = true }; PageContent.Children.Add(box); choices.Add((box, suggestion));
                Text(PageContent, "Affected untracked: " + (suggestion.AffectedUntracked.Length == 0 ? "none currently" : string.Join(", ", suggestion.AffectedUntracked.Take(20))), true);
                if (suggestion.AlreadyTracked.Length > 0) Text(PageContent, "Already tracked — remains in Git: " + string.Join(", ", suggestion.AlreadyTracked.Take(20)), true);
            }
            Text(PageContent, "Review credentials, private configuration, generated output, and large binary assets before first publishing. This review is an aid and cannot guarantee secret detection.", true);
            PageContent.Children.Add(OperationButton("Preview adding these ignore rules", OperationKind.ApplyIgnore, async () => await RequestAsync(OperationKind.ApplyIgnore, ignore: choices.Where(c => c.Box.IsChecked == true).Select(c => c.Suggestion.Pattern).ToImmutableArray())));
            PageContent.Children.Add(Action("Back to Changes", () => Navigate("Changes")));
        });
    }
    private void BuildStash()
    {
        reviewing = true; PageContent.Children.Clear(); Heading("Set unfinished work aside (stash)", "Puts your unsaved edits away on this computer so your files go back to the last saved version. You can bring them back later. Nothing is uploaded.");
        var include = new CheckBox { Content = "Include untracked files (ignored files are still excluded)", IsChecked = false }; PageContent.Children.Add(include);
        PageContent.Children.Add(OperationButton("Preview setting work aside (stash)", OperationKind.Stash, async () => await RequestAsync(OperationKind.Stash, message: "GitCompass preserved work", includeUntracked: include.IsChecked == true)));
        if (model.Snapshot is not null && stashes.TryGetValue(model.Snapshot.ProjectId, out var id))
        {
            Text(PageContent, "App-created stash identity: " + id + ". It is retained after apply for your review.", true);
            PageContent.Children.Add(OperationButton("Preview bringing set-aside work back (stash)", OperationKind.ApplyStash, async () => await RequestAsync(OperationKind.ApplyStash, target: id)));
        }
        PageContent.Children.Add(Action("Back to Changes", () => Navigate("Changes")));
    }
    private void BuildRecovery(Panel target)
    {
        Subheading(target, "Unfinished step: " + model.Snapshot!.ActiveOperation + " — needs your choice");
        Text(target, "Some files were changed in two places at once (a conflict). Open each file below in your editor, keep the lines you want, and delete the <<<<<<< ======= >>>>>>> marker lines. Then choose \"Preview marking this file as fixed\" for each one. Picture or renamed/deleted files may need help from a developer.", true);
        foreach (var file in model.Snapshot.Files.Where(f => f.Kind == FileKind.Conflicted))
        {
            var row = Row(); Text(row, file.Path); row.Children.Add(Action("Show file in folder", () => RevealProjectFile(file.Path))); row.Children.Add(OperationButton("Preview marking this file as fixed", OperationKind.Stage, async () => await RequestAsync(OperationKind.Stage, paths: ImmutableArray.Create(file.Path)))); target.Children.Add(row);
        }
        if (model.Snapshot.ActiveOperation == GitOperation.Merge)
        {
            target.Children.Add(OperationButton("Preview finishing the combine (merge)", OperationKind.ContinueMerge, async () => await RequestAsync(OperationKind.ContinueMerge)));
            target.Children.Add(OperationButton("Preview cancelling the combine (abort merge)", OperationKind.AbortMerge, async () => await RequestAsync(OperationKind.AbortMerge)));
            Text(target, "Aborting puts things back to how they were before this step started. It is not a full backup, so check your files afterwards.", true);
        }
        else Text(target, "This operation needs an external editor or developer-guided terminal recovery. GitCompass keeps its state and output visible.", true);
    }
    private void RevealProjectFile(string relative)
    {
        try { Process.Start(ProjectFileReveal.Create(model.Snapshot!.Project.RootPath, relative)); } catch (Exception e) { ShowProblem("Reveal project file", e.Message); }
    }
    private sealed record HistoryReference(string Label, string Id);
    private async void BuildHistory()
    {
        Heading("History", "Every version you or your team saved (commits), newest first. Unsaved edits are on the Changes page, not here.");
        var snapshot = model.Snapshot!; var generation = renderGeneration;
        Text(PageContent, "Your branch: " + (snapshot.Branch ?? snapshot.BranchState.ToString()) + " • last known team copy: " + (snapshot.Upstream ?? "not configured") + " • only in your branch: " + (snapshot.Ahead?.ToString() ?? "unknown") + " • only in the team copy: " + (snapshot.Behind?.ToString() ?? "unknown"), true);
        if (historyLeftId is not null && historyLeftId == historyRightId)
            AddCard(PageContent, "Viewing recorded history: " + historyLeftLabel + ". Your checked-out branch remains " + (snapshot.Branch ?? snapshot.BranchState.ToString()) + ". Viewing does not switch branches or change your files. Fetched copies reflect the last remote check.");
        else if (model.History?.Comparison is { } comparison) Text(PageContent, $"Compared branches: {historyLeftLabel ?? Short(comparison.LeftCommitId)} and {historyRightLabel ?? (comparison.RightCommitId is null ? "none" : Short(comparison.RightCommitId))}. Only in the first: {comparison.LeftOnly?.ToString() ?? "unknown"}; only in the second: {comparison.RightOnly?.ToString() ?? "unknown"}.", true);
        var branchControls = new StackPanel(); PageContent.Children.Add(branchControls);
        if (snapshot.IsShallow) AddCard(PageContent, "Limited history: this repository is shallow. Parents outside available history cannot be shown.");
        if (model.History?.Commits.Length > 0)
        {
            var commits = model.History.Commits;
            var filter = Input(PageContent, "Filter this page by commit message, author, branch label, or commit ID", historyFilter);
            var graphView = new StackPanel(); PageContent.Children.Add(graphView);
            void ShowFilteredHistory()
            {
                historyFilter = filter.Text; graphView.Children.Clear();
                var visible = commits.Where(commit => string.IsNullOrWhiteSpace(historyFilter) || $"{commit.Subject} {commit.Author} {commit.Id} {string.Join(' ', commit.Refs)}".Contains(historyFilter, StringComparison.OrdinalIgnoreCase)).ToImmutableArray();
                if (visible.Length < commits.Length) Text(graphView, $"Showing {visible.Length} of {commits.Length} commits on this loaded page. Dotted paths may end at a commit hidden by this filter; they do not imply a new parent relationship.", true, 12);
                if (visible.Length > 0) graphView.Children.Add(HistoryControl(visible, true)); else AddCard(graphView, "No loaded commits match this filter. Clear it or choose another history page.");
            }
            ShowFilteredHistory(); filter.TextChanged += (_, _) => ShowFilteredHistory();
            Text(PageContent, $"History page {historySkip / 200 + 1}; at most 200 rows are held in the view.", true);
            var pages = Row();
            if (historySkip > 0) pages.Children.Add(Action("Newer versions", async () => await HistoryPageAsync(Math.Max(0, historySkip - 200))));
            if (model.History.HasMore) pages.Children.Add(Action("Older versions", async () => await HistoryPageAsync(historySkip + 200)));
            PageContent.Children.Add(pages);
        }
        else AddCard(PageContent, "No saved versions yet. Go to Changes, tick your files, and save your first version (commit).");
        var row = Row(); row.Children.Add(Action("Learn about branches", () => ShowLesson("git-branch"))); row.Children.Add(OperationButton("See ways to combine work (merge)", OperationKind.Merge, BuildMerge)); PageContent.Children.Add(row);
        try
        {
            var branches = await workspace!.BranchesAsync(snapshot.ProjectId);
            if (model.Page != "History graph" || reviewing || renderGeneration != generation || model.Snapshot?.ProjectId != snapshot.ProjectId) return;
            var references = branches.Where(branch => !branch.IsRemote || !branch.Name.EndsWith("/HEAD", StringComparison.Ordinal)).Select(branch => new HistoryReference((branch.IsCurrent ? "Your branch: " : branch.IsRemote ? "Fetched copy: " : "Local branch: ") + branch.Name, branch.Id)).ToList();
            if (snapshot.HeadId is not null && !references.Any(reference => reference.Id == snapshot.HeadId)) references.Insert(0, new("Current version (HEAD)", snapshot.HeadId));
            if (references.Count == 0) return;
            var referenceRow = Columns(2); var firstPanel = new StackPanel { Margin = new(0, 0, 14, 0) }; var secondPanel = new StackPanel(); referenceRow.Children.Add(firstPanel); Grid.SetColumn(secondPanel, 1); referenceRow.Children.Add(secondPanel);
            Text(firstPanel, "First branch"); Text(secondPanel, "Second branch");
            var left = new ComboBox { ItemsSource = references, DisplayMemberPath = "Label", SelectedItem = references.FirstOrDefault(reference => reference.Id == (historyLeftId ?? snapshot.HeadId)) ?? references[0] };
            var right = new ComboBox { ItemsSource = references, DisplayMemberPath = "Label", SelectedItem = references.FirstOrDefault(reference => reference.Id == (historyRightId ?? snapshot.UpstreamId)) ?? references[0] };
            AutomationProperties.SetName(left, "First history comparison branch"); AutomationProperties.SetName(right, "Second history comparison branch"); firstPanel.Children.Add(left); secondPanel.Children.Add(right); branchControls.Children.Add(referenceRow);
            var actions = Row(); actions.Children.Add(Action("Compare selected branches", async () =>
            {
                if (left.SelectedItem is not HistoryReference l || right.SelectedItem is not HistoryReference r) return;
                await WorkAsync(async () =>
                {
                    var fresh = await workspace.RefreshAsync(snapshot.ProjectId); model.Snapshot = fresh;
                    var comparisonPage = await workspace.HistoryAsync(snapshot.ProjectId, new HistoryRequest(l.Id, r.Id), activeCancellation!.Token);
                    historyLeftId = l.Id; historyRightId = r.Id; historyLeftLabel = l.Label; historyRightLabel = r.Label; historySkip = 0; model.History = comparisonPage; RenderPage();
                });
            }));
            actions.Children.Add(Action("Show current branch and team copy", async () =>
            {
                historyLeftId = historyRightId = historyLeftLabel = historyRightLabel = null; historySkip = 0;
                await WorkAsync(async () => { model.Snapshot = await workspace.RefreshAsync(snapshot.ProjectId); model.History = await workspace.HistoryAsync(snapshot.ProjectId); RenderPage(); });
            })); branchControls.Children.Add(actions);
        }
        catch (Exception error) { if (model.Page == "History graph" && renderGeneration == generation) ShowProblem("Read history comparison branches", error.Message); }
    }
    private async Task HistoryPageAsync(int skip)
    {
        if (workspace is null || model.Snapshot is null || model.History is null) return;
        var snapshot = model.Snapshot; var anchor = model.History.Anchor;
        await WorkAsync(async () =>
        {
            var fresh = await workspace.RefreshAsync(snapshot.ProjectId);
            if (historyLeftId is null && (fresh.HeadId != snapshot.HeadId || fresh.UpstreamId != snapshot.UpstreamId)) { model.Snapshot = fresh; model.History = await workspace.HistoryAsync(snapshot.ProjectId); historySkip = 0; Banner("History refs changed. The anchored first page has been refreshed."); }
            else
            {
                model.Snapshot = fresh;
                var page = historyLeftId is null ? await workspace.HistoryAsync(snapshot.ProjectId, skip) : await workspace.HistoryAsync(snapshot.ProjectId, new HistoryRequest(historyLeftId, historyRightId, skip));
                if (page.Anchor != anchor) throw new StalePreviewException("History changed while paging. Refresh the graph.");
                model.History = page with { Commits = page.Commits.DistinctBy(c => c.Id).ToImmutableArray() }; historySkip = skip;
            }
            RenderPage();
        });
    }
    private sealed record CommitRow(CommitNode Commit, string Label, string AccessibleLabel);
    private FrameworkElement HistoryControl(ImmutableArray<CommitNode> commits, bool details)
    {
        var panel = new StackPanel(); var grid = new Grid(); var graph = new CommitGraph(commits);
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(graph.Width) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.SizeChanged += (_, _) => grid.ColumnDefinitions[0].Width = new(Math.Min(graph.Width, Math.Max(48, grid.ActualWidth - 200)));
        var visibleHeight = Math.Min(commits.Length * CommitGraph.RowHeight, details ? 430 : 340); grid.Height = visibleHeight;
        var graphCanvas = new Canvas { Height = visibleHeight, ClipToBounds = true }; graphCanvas.Children.Add(graph); grid.Children.Add(graphCanvas);
        var list = new ListBox { BorderThickness = new(0), Height = visibleHeight };
        var labelFactory = new FrameworkElementFactory(typeof(TextBlock)); labelFactory.SetBinding(TextBlock.TextProperty, new Binding("Label")); labelFactory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap); labelFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); labelFactory.SetValue(TextBlock.FontSizeProperty, 12.0); list.ItemTemplate = new DataTemplate { VisualTree = labelFactory };
        VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Pixel); ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        var itemStyle = new Style(typeof(ListBoxItem), (Style)FindResource(typeof(ListBoxItem))); itemStyle.Setters.Add(new Setter(HeightProperty, CommitGraph.RowHeight)); itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 3, 6, 3))); itemStyle.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding("AccessibleLabel"))); list.ItemContainerStyle = itemStyle;
        list.ItemsSource = commits.Select(commit => new CommitRow(commit, commit.Subject + (commit.Refs.Length == 0 ? "" : "  [" + DescribeRefs(commit.Refs) + "]") + "\n" + Short(commit.Id) + "  •  " + commit.Author + "  •  " + commit.Date.ToLocalTime().ToString("g"), commit.Subject + ". Commit " + commit.Id + ". Parents " + (commit.Parents.Length == 0 ? "none" : string.Join(", ", commit.Parents)) + ". Refs " + string.Join(", ", commit.Refs))).ToArray();
        list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, args) => graph.RenderTransform = new TranslateTransform(0, -args.VerticalOffset)));
        Grid.SetColumn(list, 1); grid.Children.Add(list);
        panel.Children.Add(grid);
        Text(panel, "Circles are saved commits. Each colored line follows recorded parents; dotted lines continue beyond this page. Your branch is local; a fetched copy is the last known remote version.", true, 11);
        if (details)
        {
            var info = new StackPanel(); var diff = Mono("Select a commit to review its recorded changes.", 220); panel.Children.Add(info); panel.Children.Add(diff);
            list.SelectionChanged += async (_, _) =>
            {
                if (list.SelectedItem is not CommitRow row) return;
                var commit = row.Commit;
                info.Children.Clear(); Text(info, commit.Subject, false, 17, FontWeights.SemiBold); Text(info, "Commit " + commit.Id + "\nParents: " + string.Join(", ", commit.Parents) + "\n" + commit.Author + " • " + commit.Date.ToLocalTime().ToString("g"), true);
                try { var result = await workspace!.DiffAsync(model.Snapshot!.ProjectId, new(DiffKind.Commit, CommitId: commit.Id)); diff.Text = result.Text + (result.Truncated ? "\n[Diff truncated]" : "") + (result.Problem is null ? "" : "\n" + result.Problem); }
                catch (Exception e) { diff.Text = e.Message; }
            };
        }
        return panel;
    }
    private void BuildMerge()
    {
        reviewing = true; PageContent.Children.Clear(); Heading("Combine your work with the team's (merge)", "You and your team both saved new versions. Combining them (merge) keeps both. GitCompass never overwrites the online copy (no force-push).");
        var snapshot = model.Snapshot!; AddCard(PageContent, "Current branch: " + snapshot.Branch + "\nTeam copy last checked (fetched upstream): " + (snapshot.Upstream ?? "unknown") + "\nReviewed target: " + (snapshot.UpstreamId ?? "unavailable") + "\nSave or set aside (stash) your unsaved edits first. Cancelling a combine may not bring back unsaved edits.");
        if (snapshot.UpstreamId is not null) PageContent.Children.Add(OperationButton("Preview combining the team's new versions (merge)", OperationKind.Merge, async () => await RequestAsync(OperationKind.Merge, target: snapshot.UpstreamId)));
        PageContent.Children.Add(Action("Learn about combining work and fixing clashes (merge, conflict)", () => ShowLesson("git-merge"))); PageContent.Children.Add(Action("Back to history", () => Navigate("History graph")));
    }
    private async void BuildBranches()
    {
        Heading("Branches", "Lines of work (branches) let you try ideas without changing the main one. You can look at any of them safely. Switching to another one changes the files in your folder, so it needs Allow changes.");
        var id = model.Snapshot!.ProjectId; var generation = renderGeneration;
        try
        {
            var branches = await workspace!.BranchesAsync(id); if (model.Page != "Branches" || model.Snapshot?.ProjectId != id || generation != renderGeneration) return;
            var list = new ListBox { Height = 235 };
            AutomationProperties.SetName(list, "Your lines of work (branches)");
            foreach (var branch in branches) list.Items.Add(new ListBoxItem { Content = (branch.IsCurrent ? "Current  " : "") + branch.Name + (branch.Upstream is null ? "" : " → " + branch.Upstream) + (branch.IsRemote ? "  (fetched remote)" : ""), Tag = branch, Padding = new(9) });
            PageContent.Children.Add(list);
            var viewHistory = Action("See saved versions on this line of work", async () =>
            {
                if (list.SelectedItem is not ListBoxItem { Tag: BranchInfo selected }) { Banner("Pick a line of work (branch) to see its saved versions."); return; }
                await WorkAsync(async () =>
                {
                    model.Snapshot = await workspace.RefreshAsync(id, activeCancellation!.Token);
                    var history = await workspace.HistoryAsync(id, new HistoryRequest(selected.Id, selected.Id), activeCancellation.Token);
                    historyLeftId = historyRightId = selected.Id;
                    historyLeftLabel = historyRightLabel = (selected.IsRemote ? "Fetched copy: " : "Local branch: ") + selected.Name;
                    historySkip = 0; historyFilter = ""; model.History = history;
                    Navigate("History graph");
                });
            }, true);
            viewHistory.IsEnabled = false;
            viewHistory.ToolTip = "Shows the saved versions on the chosen line of work. Your files don't change.";
            ToolTipService.SetShowOnDisabled(viewHistory, true);
            list.SelectionChanged += (_, _) => viewHistory.IsEnabled = list.SelectedItem is ListBoxItem { Tag: BranchInfo };
            PageContent.Children.Add(viewHistory);
            Text(PageContent, "Picking a row only chooses what to look at. Team copies show what was online when you last checked for updates (fetch).", true);
            if (branches.Length == 0) AddCard(PageContent, "No lines of work yet. Save your first version (commit) on the Changes page, then you can start a new line of work (branch).");
            PageContent.Children.Add(OperationButton("Preview switching to this line of work (branch)", OperationKind.SwitchBranch, async () => { if (list.SelectedItem is ListBoxItem { Tag: BranchInfo branch } && !branch.IsRemote) await RequestAsync(OperationKind.SwitchBranch, branch: branch.Name); else Banner("Pick a line of work on this computer first."); }));
            var newName = Input(PageContent, "Name for the new line of work (branch), e.g. try-new-colors", ""); var baseRef = Input(PageContent, "Base commit or branch", model.Snapshot.HeadId ?? "");
            PageContent.Children.Add(OperationButton("Preview creating a new line of work (branch)", OperationKind.CreateBranch, async () => await RequestAsync(OperationKind.CreateBranch, branch: newName.Text, target: baseRef.Text)));
            var intent = Input(PageContent, "Which line of work should this task use? (optional)", model.Snapshot.Project.IntendedBranch ?? "");
            PageContent.Children.Add(Action("Remember this line of work for the task", async () => { workspace.SetIntendedBranch(id, intent.Text); await RefreshAsync(); })); Text(PageContent, "If you set this, GitCompass warns you when you're on a different line of work. Save or set aside your edits before switching.", true);
        }
        catch (Exception e) { ShowProblem("Read branches", e.Message); }
    }
    private void BuildPullRequests()
    {
        Heading("Pull requests", "A review request (pull request) asks your team on GitHub to look at your uploaded work and add it to theirs. It is not the same as Get updates (pull).");
        var destination = model.Snapshot!.Push; var match = Regex.Match(destination?.Url ?? "", @"github\.com[:/]([^/]+)/(.+?)(?:\.git)?$");
        var owner = Input(PageContent, "Target repository owner", match.Success ? match.Groups[1].Value : ""); var repository = Input(PageContent, "Target repository name", match.Success ? match.Groups[2].Value : "");
        if (string.IsNullOrWhiteSpace(EffectiveGitHubClientId))
        {
            AddCard(PageContent, "Review requests aren't available in this test version of GitCompass yet. Saving versions and uploading to GitHub still work.");
        }
        var output = new StackPanel();
        async Task ReadStatus(int number)
        {
            await WorkAsync(async () =>
            {
                using var lease = workspace!.Access.Acquire(model.Snapshot!.ProjectId, OperationKind.GitHubRead);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Revoked, activeCancellation!.Token);
                var status = await HostedOperationAsync(model.Snapshot.ProjectId, OperationKind.GitHubRead, "Read GitHub pull request status", () => CreateGitHubClient(model.Snapshot.ProjectId).ReadPullRequestStatusAsync(owner.Text.Trim(), repository.Text.Trim(), number, linked.Token));
                var card = new StackPanel(); Subheading(card, "#" + status.Request.Number + " " + status.Request.Title);
                Text(card, "Request state: " + status.Request.State + " • merge state: " + (status.MergeState ?? "not yet reported"), true);
                Subheading(card, "Reviews"); Text(card, status.Reviews.Count == 0 ? "No reviews were returned." : string.Join("\n", status.Reviews));
                Subheading(card, "Checks"); Text(card, status.Checks.Count == 0 ? "No checks were returned." : string.Join("\n", status.Checks));
                if (status.Unavailable.Count > 0) Text(card, "Some details are unavailable: " + string.Join("\n", status.Unavailable), true);
                card.Children.Add(Action("Open pull request in browser", () => OpenLink(status.Request.HtmlUrl))); AddCard(output, card); output.BringIntoView();
            });
        }
        var signIn = Action(GitHubSignInLabel, SignIn); signIn.IsEnabled = !string.IsNullOrWhiteSpace(EffectiveGitHubClientId); PageContent.Children.Add(signIn);
        PageContent.Children.Add(OperationButton("See review requests (pull requests)", OperationKind.GitHubRead, async () =>
        {
            await WorkAsync(async () =>
            {
                using var lease = workspace!.Access.Acquire(model.Snapshot!.ProjectId, OperationKind.GitHubRead);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Revoked, activeCancellation!.Token);
                var list = await HostedOperationAsync(model.Snapshot.ProjectId, OperationKind.GitHubRead, "Read GitHub pull requests", () => CreateGitHubClient(model.Snapshot.ProjectId).ReadPullRequestsAsync(owner.Text.Trim(), repository.Text.Trim(), linked.Token));
                output.Children.Clear();
                if (list.Count == 0) Text(output, "No open review requests (pull requests) for this project right now.", true);
                foreach (var pr in list)
                {
                    var card = new StackPanel(); Text(card, $"#{pr.Number}  {pr.Title}", false, 16, FontWeights.SemiBold); Text(card, $"{pr.Head} → {pr.Base} • {pr.State} • {(pr.Draft ? "Draft" : "Ready")} • mergeability {(pr.Mergeable.HasValue ? (pr.Mergeable.Value ? "available" : "conflicts reported") : "pending / unknown")}", true); card.Children.Add(Action("Open pull request in browser", () => OpenLink(pr.HtmlUrl))); card.Children.Add(OperationButton("Check status, reviews and tests", OperationKind.GitHubRead, async () => await ReadStatus(pr.Number))); AddCard(output, card);
                }
            });
        }));
        var lookupNumber = Input(PageContent, "Review request number (pull request #), also finds closed ones", "");
        PageContent.Children.Add(OperationButton("Check this review request's status", OperationKind.GitHubRead, async () => { if (int.TryParse(lookupNumber.Text, out var number) && number > 0) await ReadStatus(number); else Banner("Type the request's number, for example 12."); })); PageContent.Children.Add(output);
        Subheading(PageContent, "Prepare a pull request");
        var title = Input(PageContent, "Title", ""); var body = Input(PageContent, "Description", "", true);
        var sourceOwner = Input(PageContent, "Source repository owner", match.Success ? match.Groups[1].Value : ""); var sourceRepository = Input(PageContent, "Source repository name (forks can use a different name)", match.Success ? match.Groups[2].Value : "");
        var head = Input(PageContent, "Your uploaded line of work (source branch)", model.Snapshot.Branch ?? ""); var target = Input(PageContent, "Target branch — choose explicitly", ""); var draft = new CheckBox { Content = "Create as draft", IsChecked = true }; PageContent.Children.Add(draft);
        PageContent.Children.Add(OperationButton("Preview sending the review request (pull request)", OperationKind.GitHubWrite, async () =>
        {
            await RefreshAsync(false);
            var snapshot = model.Snapshot!; workspace!.Access.Authorize(snapshot.ProjectId, OperationKind.GitHubWrite);
            if (snapshot.HeadId is null || snapshot.BranchState != BranchState.Attached || head.Text.Trim() != snapshot.Branch) { Banner("Pick the line of work (branch) you are on now as the source, and upload (push) it before asking for a review."); return; }
            var publishedOwner = sourceOwner.Text.Trim(); var publishedRepository = sourceRepository.Text.Trim(); var publishedBranch = head.Text.Trim(); var repositoryOwner = owner.Text.Trim(); var repositoryName = repository.Text.Trim();
            using (var verificationLease = workspace.Access.Acquire(snapshot.ProjectId, OperationKind.GitHubRead))
                if (!await HostedOperationAsync(snapshot.ProjectId, OperationKind.GitHubRead, "Verify the published GitHub source version", () => CreateGitHubClient(snapshot.ProjectId).VerifyPublishedCommitAsync(publishedOwner, publishedRepository, publishedBranch, snapshot.HeadId, verificationLease.Revoked))) { Banner("Your newest saved version isn't online on that branch yet. Upload (push) it there first, then come back to make the review request (pull request)."); return; }
            if (model.Snapshot?.ProjectId != snapshot.ProjectId || model.Page != "Pull requests") return;
            var request = new PullRequestDraft(title.Text, body.Text, publishedOwner + ":" + publishedBranch, target.Text, draft.IsChecked == true, publishedRepository); var fingerprint = snapshot.Fingerprint; var generation = workspace.Access.Get(snapshot.ProjectId).Generation;
            reviewing = true; PageContent.Children.Clear(); Heading("Review pull request", "Sending this creates a review request on GitHub. It does not combine (merge) anything yet."); AddCard(PageContent, $"Source repository: {publishedOwner}/{publishedRepository}\nPublished source branch: {publishedBranch}\nVerified source commit: {snapshot.HeadId}\nTarget repository: {repositoryOwner}/{repositoryName}\nTarget branch: {request.Base}\nTitle: {request.Title}\nDraft: {request.Draft}\nAccess: Allow changes\n\n{request.Body}");
            PageContent.Children.Add(Action("Send this review request (pull request)", async () =>
            {
                await WorkAsync(async () =>
                {
                    workspace.Access.Authorize(snapshot.ProjectId, OperationKind.GitHubWrite, generation); var fresh = await workspace.RefreshAsync(snapshot.ProjectId);
                    if (fresh.Fingerprint != fingerprint) throw new StalePreviewException("Your work changed after the preview. Please preview the review request again.");
                    using var lease = workspace.Access.Acquire(snapshot.ProjectId, OperationKind.GitHubWrite, generation); using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Revoked, activeCancellation!.Token);
                    if (!await HostedOperationAsync(snapshot.ProjectId, OperationKind.GitHubRead, "Recheck the published GitHub source version", () => CreateGitHubClient(snapshot.ProjectId).VerifyPublishedCommitAsync(publishedOwner, publishedRepository, publishedBranch, snapshot.HeadId, linked.Token))) throw new StalePreviewException("The uploaded work changed after the preview. Please preview again.");
                    var pr = await HostedOperationAsync(snapshot.ProjectId, OperationKind.GitHubWrite, "Submit the reviewed GitHub pull request", () => CreateGitHubClient(snapshot.ProjectId).CreatePullRequestAsync(repositoryOwner, repositoryName, request, linked.Token)); Banner("Pull request #" + pr.Number + " created or matching existing request found."); reviewing = false; RenderPage();
                });
            }, true)); PageContent.Children.Add(Action("Cancel review", () => Navigate("Pull requests")));
        }));
    }
    private GitHubClient CreateGitHubClient(Guid projectId)
    {
        if (!githubClients.TryGetValue(projectId, out var client)) githubClients[projectId] = client = new(http, tokens, () => Permission(projectId, OperationKind.GitHubRead), () => Permission(projectId, OperationKind.GitHubWrite));
        if (!string.IsNullOrWhiteSpace(EffectiveGitHubClientId)) client.RefreshTokensAsync = new GitHubDeviceAuth(http, tokens, EffectiveGitHubClientId!).RefreshAsync;
        return client;
    }
    private async Task<T> HostedOperationAsync<T>(Guid projectId, OperationKind kind, string purpose, Func<Task<T>> operation)
    {
        var id = Guid.NewGuid(); AddEvent(new(id, projectId, DateTimeOffset.Now, OperationState.Starting, "hosted-purpose", purpose, kind));
        AddEvent(new(id, projectId, DateTimeOffset.Now, OperationState.Running, "hosted", purpose + " is running.", kind));
        try
        {
            var result = await operation();
            var mismatched = result is bool matched && !matched;
            AddEvent(new(id, projectId, DateTimeOffset.Now, mismatched ? OperationState.Stale : OperationState.Succeeded, "hosted", mismatched ? purpose + ": the published source differs; submission is blocked until a new review." : purpose + " completed.", kind)); return result;
        }
        catch (Exception error)
        {
            var state = error is GitHubException { Failure: GitHubFailure.OutcomeUnknown } ? OperationState.OutcomeUnknown : error is AccessDeniedException or UnauthorizedAccessException ? OperationState.Denied : error is OperationCanceledException ? OperationState.Canceled : OperationState.Failed;
            var category = error is GitHubException hosted ? hosted.Failure.ToString() : error is OperationCanceledException ? "Canceled" : error is AccessDeniedException or UnauthorizedAccessException ? "Access denied" : "Could not complete";
            AddEvent(new(id, projectId, DateTimeOffset.Now, state, "hosted", purpose + ": " + category + ". Review the Problems view and current repository state.", kind)); throw;
        }
    }
    private bool Permission(Guid id, OperationKind kind) { try { workspace!.Access.Authorize(id, kind); return true; } catch { return false; } }
    private async void SignIn()
    {
        if (string.IsNullOrWhiteSpace(EffectiveGitHubClientId)) { Banner("Review requests aren't available in this test version yet. Uploading to GitHub still works."); return; }
        authenticationCancellation?.Cancel(); authenticationCancellation = new();
        try
        {
            var auth = new GitHubDeviceAuth(http, tokens, EffectiveGitHubClientId!); var device = await auth.BeginAsync(authenticationCancellation.Token);
            reviewing = true; PageContent.Children.Clear(); Heading(GitHubSignInLabel, "Sign-in happens in your web browser. GitCompass never asks for your GitHub password."); AddCard(PageContent, "Enter code " + device.UserCode + " at " + device.VerificationUri + "\nThis request expires at " + device.ExpiresAt.ToLocalTime().ToString("t") + ".");
            PageContent.Children.Add(Action("Open GitHub sign-in", () => OpenLink(device.VerificationUri.ToString()), true)); PageContent.Children.Add(Action("Cancel sign-in", () => { authenticationCancellation.Cancel(); Navigate("Pull requests"); }));
            await auth.CompleteAsync(device, authenticationCancellation.Token); Banner("Signed in to GitHub. Uploads may still ask you to sign in once in a separate Git window."); Navigate("Pull requests");
        }
        catch (OperationCanceledException) { Banner("GitHub sign-in canceled."); }
        catch (Exception e) { ShowProblem("GitHub sign-in", e.Message); }
    }
    private void BuildLearn(string? initial = null)
    {
        Heading("Learn", "Start with what Git remembers, then try saving one version. These explanations work offline.");
        BuildGuideEntry(PageContent);
        BuildReplayEntries(PageContent);
        if (learning is null) { AddCard(PageContent, setupError ?? "The local learning content is missing."); return; }
        var lessonPanel = new StackPanel(); PageContent.Children.Add(lessonPanel);
        var finder = new StackPanel();
        var search = Input(finder, "Search tasks, concepts, and commands", ""); var list = new ListBox { Height = 170, DisplayMemberPath = "Title" }; AutomationProperties.SetName(list, "Available offline lessons"); finder.Children.Add(list);
        PageContent.Children.Add(new Expander { Header = "Find another lesson or command", Content = finder });
        void Fill() => list.ItemsSource = learning.Search(search.Text).ToArray(); Fill(); search.TextChanged += (_, _) => Fill();
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is LearningEntry entry) { RenderLesson(lessonPanel, entry); lessonPanel.BringIntoView(); } };
        var selected = initial is null ? learning.Find("basics") : learning.Find(initial); if (selected is not null) { list.SelectedItem = selected; RenderLesson(lessonPanel, selected); }
        PageContent.Children.Add(Action("Create a separate practice folder", async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "GitCompass-Practice", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]); Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "practice.txt"), "A disposable learning project.\n"); await OpenProjectAsync(root);
        }));
        Text(PageContent, "Practice in a disposable folder before using these steps on work you care about.", true);
        if (inventory is not null)
        {
            var inventoryPanel = new StackPanel(); Text(inventoryPanel, "Learning version " + learning.Manifest.Version + " • " + learning.Manifest.SupportedCoreCommands.Length + " declared core commands", true); Text(inventoryPanel, "Installed offline inventory: " + inventory.Version, true);
            Text(inventoryPanel, "Installed commands outside the declared supported reference: " + string.Join(", ", inventory.UnsupportedInstalledCommands), true);
            Text(inventoryPanel, "Declared commands not listed by this Git help inventory (absence here does not prove that their executables are unavailable): " + string.Join(", ", inventory.DeclaredCommandsMissingFromInventory), true);
            if (inventory.Error is not null) Text(inventoryPanel, inventory.Error, true);
            PageContent.Children.Add(new Expander { Header = "View installed command inventory and reference coverage", Content = inventoryPanel });
        }
    }
    private void ShowLesson(string id) { reviewing = false; model.Page = "Learn"; SelectNavigation("Learn"); PageContent.Children.Clear(); BuildLearn(id); }
    private void RenderLesson(Panel panel, LearningEntry entry)
    {
        panel.Children.Clear(); Subheading(panel, entry.Title);
        if (GitGlossary.Terms.FirstOrDefault(t => t.LessonId == entry.Id) is { } word) Text(panel, "In everyday words: " + word.Everyday + " (" + word.Term + ") — " + word.Plain, false, 14, FontWeights.SemiBold);
        Text(panel, entry.Purpose); Text(panel, "Before you start: " + entry.Prerequisites, true); AddCard(panel, "What changes: " + entry.Effects + "\nWhen to pause: " + entry.WhenToPause);
        if (entry.Id == "basics") Text(panel, entry.Example); else panel.Children.Add(Mono(entry.Example, 115));
        Text(panel, "Expected outcome: " + entry.Outcome, true); Text(panel, "Practice: " + entry.Practice, true);
        if (GitGlossary.DocUrlForLesson(entry.Id) is { } doc) panel.Children.Add(DocChip(doc));
    }
    private void BuildSettings()
    {
        Heading("Settings", "GitCompass " + (typeof(ShellWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0") + " • Local preferences, appearance, connections, and opt-in team checks.");
        if (setupError is not null) AddCard(PageContent, setupError);
        Subheading(PageContent, "Walkthrough and welcome");
        BuildReplayEntries(PageContent);
        if (workspace is null)
        {
            var prerequisite = new StackPanel();
            Subheading(prerequisite, "Install Git to remember project versions");
            Text(prerequisite, "Git is the local tool GitCompass uses to save version history. You do not need a GitHub account to save versions on this computer. After installing Git for Windows, return here and check again.", true);
            prerequisite.Children.Add(Action("Get Git for Windows", () => OpenLink("https://git-scm.com/downloads/win")));
            prerequisite.Children.Add(Action("Check installation and continue", async () =>
            {
                if (model.Busy) return;
                setupError = null; await InitializeAsync();
                if (workspace is null) { Navigate("Settings"); return; }
                if (pendingProjectFolder is { } selected) { pendingProjectFolder = null; await OpenProjectAsync(selected); }
                else Navigate("Overview");
            }, true));
            AddCard(PageContent, prerequisite);
        }
        if (damagedSettings is not null) PageContent.Children.Add(Action("Back up the broken settings file and start fresh", () =>
        {
            var backup = damagedSettings + ".preserved-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"); File.Copy(damagedSettings, backup, overwrite: false); damagedSettings = null; setupError = null; Banner("Damaged settings copied to a separate recovery file. Review preferences before saving defaults.");
        }));
        var theme = new ComboBox { ItemsSource = new[] { "System", "Light", "Dark" }, SelectedItem = settings.Theme, Width = 170, HorizontalAlignment = HorizontalAlignment.Left };
        PageContent.Children.Add(SettingsRow("Appearance", "Follow Windows, or choose a light or dark appearance. Your choice saves automatically.", theme));
        theme.SelectionChanged += async (_, _) =>
        {
            var choice = theme.SelectedItem?.ToString() ?? "System";
            ApplyTheme(choice);
            settings = settings with { Theme = choice };
            if (damagedSettings is not null) { Banner("Appearance is previewed. Preserve the damaged settings file before saving preferences."); return; }
            try { await store.SaveSettingsAsync(settings); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { Banner("Your appearance changed, but could not be saved for next time. " + error.Message); }
        };
        AutomationProperties.SetName(theme, "Appearance theme");
        var materials = new ComboBox
        {
            ItemsSource = new[] { new WindowMaterialChoice("Mica", "Mica (Windows default)"), new WindowMaterialChoice("Acrylic", "Glass (Acrylic)"), new WindowMaterialChoice("Solid", "Solid") },
            DisplayMemberPath = "Label", SelectedValuePath = "Key", SelectedValue = settings.WindowMaterial,
            Width = 230, HorizontalAlignment = HorizontalAlignment.Left
        };
        AutomationProperties.SetName(materials, "Window background material");
        PageContent.Children.Add(SettingsRow("Window background", "Use a subtle Windows tint, frosted glass, or a solid background. Text and content cards stay clear. Your choice saves automatically.", materials));
        materials.SelectionChanged += async (_, _) =>
        {
            settings = settings with { WindowMaterial = materials.SelectedValue?.ToString() ?? "Mica" };
            ApplyTheme(appliedTheme);
            if (damagedSettings is not null) { Banner("The background is previewed. Preserve the damaged settings file before saving preferences."); return; }
            try { await store.SaveSettingsAsync(settings); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { Banner("Your background changed, but could not be saved for next time. " + error.Message); }
        };
        var materialStatus = new TextBlock { FontSize = 12, Margin = new(2, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        materialStatus.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        materialStatus.SetBinding(TextBlock.TextProperty, new Binding(nameof(WindowsBackdrop.Status)) { Source = backdrop });
        AutomationProperties.SetName(materialStatus, "Window background effect status");
        PageContent.Children.Add(materialStatus);
        PageContent.Children.Add(SettingsRow("Try a different design", "Explore a Liquid Glass-inspired page with sample project data. Changes inside the preview only affect its appearance.", Action("Open Liquid Glass preview", () => Navigate("Liquid Glass preview"))));
        Subheading(PageContent, "Check for team updates");
        var interval = new TextBox { Text = settings.FetchIntervalMinutes.ToString(), Width = 100, MinHeight = 36 };
        AutomationProperties.SetName(interval, "How often to check for team updates, in minutes (1–1440)");
        PageContent.Children.Add(SettingsRow("Check interval", "Minutes between automatic checks, from 1 to 1440. Checks require an enabled team connection.", interval));
        var canMonitor = model.Snapshot?.Project.Kind is RepositoryKind.WorkingTree or RepositoryKind.LinkedWorktree && model.Snapshot.HasRemotes && model.Snapshot.Upstream is not null;
        var monitoring = new CheckBox { Content = "Enabled", IsChecked = canMonitor && model.Snapshot?.Project.MonitoringEnabled == true, IsEnabled = canMonitor, ToolTip = canMonitor ? "Requires Monitor only or Allow changes." : "Select a project with a configured team connection first." };
        AutomationProperties.SetName(monitoring, "Enable periodic remote checks for the current project in this session");
        PageContent.Children.Add(SettingsRow("Automatic team checks", "Check the team's saved history during this session. These checks never apply updates to your files.", monitoring));
        ToolTipService.SetShowOnDisabled(monitoring, true);
        var mute = new CheckBox { Content = "Muted", IsChecked = settings.MuteMonitoringNotifications };
        AutomationProperties.SetName(mute, "Mute team-update notifications");
        PageContent.Children.Add(SettingsRow("Team-update notifications", "Mute update notices while permitted checks continue. Problems and operation results stay visible.", mute));
        var quietHours = new CheckBox { Content = "Enabled", IsChecked = settings.QuietHoursStart.HasValue };
        AutomationProperties.SetName(quietHours, "Use quiet hours for team-update notifications");
        PageContent.Children.Add(SettingsRow("Quiet hours", "Pause team-update notices during these local hours. Overnight periods are supported.", quietHours));
        var quietRow = Columns(2); var quietStartPanel = new StackPanel { Margin = new(0, 0, 18, 0) }; var quietEndPanel = new StackPanel(); quietRow.Children.Add(quietStartPanel); Grid.SetColumn(quietEndPanel, 1); quietRow.Children.Add(quietEndPanel);
        var quietStart = Input(quietStartPanel, "Start hour (0–23, local time)", (settings.QuietHoursStart ?? 22).ToString()); var quietEnd = Input(quietEndPanel, "End hour (0–23, local time)", (settings.QuietHoursEnd ?? 8).ToString()); PageContent.Children.Add(quietRow);
        void SetQuietInputs() => quietRow.IsEnabled = quietHours.IsChecked == true; quietHours.Checked += (_, _) => SetQuietInputs(); quietHours.Unchecked += (_, _) => SetQuietInputs(); SetQuietInputs();
        var advanced = new StackPanel();
        var git = Input(advanced, "Where Git is installed (leave blank and GitCompass will find it)", settings.GitExecutablePath ?? "");
        advanced.Children.Add(Action("Select Git executable", () => { var picker = new OpenFileDialog { Title = "Select Git for Windows", Filter = "Git executable|git.exe" }; if (picker.ShowDialog(this) == true) git.Text = picker.FileName; }));
        Text(advanced, "Detected: " + (DependencyProbe.FindGitExecutable() ?? "Git not found") + "\nHost: " + Environment.OSVersion.VersionString + " • " + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture, true);
        Text(advanced, "Only needed for review requests (pull requests) on GitHub. Most people can leave this empty. If your team gave you a GitHub App client ID, paste it here. Never type a password here.", true);
        var client = Input(advanced, "Registered GitHub App public client ID", settings.GitHubClientId ?? "");
        if (string.IsNullOrWhiteSpace(settings.GitHubClientId) && ShippedGitHubClientId is not null) Text(advanced, "This build includes a GitHub App client ID, so sign-in works without entering one. Enter a different ID here only to use your own app registration.", true);
        PageContent.Children.Add(new Expander { Header = "Advanced connection setup", Content = advanced, IsExpanded = workspace is null });
        PageContent.Children.Add(Action("Save settings", async () =>
        {
            if (damagedSettings is not null) { Banner("Preserve the damaged settings file using the recovery action before saving defaults."); return; }
            if (!int.TryParse(interval.Text, out var minutes) || minutes is < 1 or > 1440) { Banner("Choose a remote check interval from 1 to 1440 minutes."); return; }
            int? startHour = null, endHour = null;
            if (quietHours.IsChecked == true)
            {
                if (!int.TryParse(quietStart.Text, out var start) || !int.TryParse(quietEnd.Text, out var end) || start is < 0 or > 23 || end is < 0 or > 23 || start == end) { Banner("Choose different start and end hours from 0 to 23. Quiet hours may cross midnight."); return; }
                startHour = start; endHour = end;
            }
            settings = settings with { Theme = theme.SelectedItem?.ToString() ?? "Light", GitExecutablePath = string.IsNullOrWhiteSpace(git.Text) ? null : git.Text, GitHubClientId = string.IsNullOrWhiteSpace(client.Text) ? null : client.Text.Trim(), FetchIntervalMinutes = minutes, MuteMonitoringNotifications = mute.IsChecked == true, QuietHoursStart = startHour, QuietHoursEnd = endHour };
            await store.SaveSettingsAsync(settings);
            if (scheduler is not null) await scheduler.DisposeAsync(); StartScheduler();
            if (model.Snapshot is not null && workspace is not null) { if (monitoring.IsChecked == true) workspace.Access.Authorize(model.Snapshot.ProjectId, OperationKind.Fetch); workspace.SetMonitoring(model.Snapshot.ProjectId, monitoring.IsChecked == true); model.Snapshot = await workspace.RefreshAsync(model.Snapshot.ProjectId); }
            Banner("Settings saved on this computer. If Git was missing, use Check installation and continue. Changed Git paths take effect when reconnecting or restarting; project write access is never restored after restart.");
        }, true));
        PageContent.Children.Add(Action("Sign out of GitHub API", async () => { authenticationCancellation?.Cancel(); await tokens.ClearAsync(); Banner("GitHub API tokens cleared from protected local storage."); }));
        var row = Row(); row.Children.Add(Action("Git installation guidance", () => OpenLink("https://git-scm.com/download/win"))); row.Children.Add(Action("WebView2 prerequisite guidance", () => OpenLink("https://developer.microsoft.com/microsoft-edge/webview2/"))); PageContent.Children.Add(row);
    }
    private void ApplyTheme(string theme)
    {
        appliedTheme = theme;
        var dark = UsesDarkAppearance(theme);
#pragma warning disable WPF0001 // Pinned .NET 10 Fluent theme selection; validate again when the SDK changes.
        System.Windows.Application.Current.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
        ThemeMode = dark ? System.Windows.ThemeMode.Dark : System.Windows.ThemeMode.Light;
#pragma warning restore WPF0001
        var accent = dark ? SystemColors.AccentColorLight1 : SystemColors.AccentColor;
        var colors = (string[])(dark ? ThemeContrast.Dark : ThemeContrast.Light).Clone();
        // The Windows accent is used for links and focus text; nudge it until it meets WCAG AA (4.5:1) on every app background.
        colors[5] = ThemeContrast.EnsureReadable(ThemeContrast.Hex(accent.R, accent.G, accent.B), dark, colors[0], colors[1], colors[6]);
        var names = ThemeContrast.Names;
        for (var i = 0; i < names.Length; i++) System.Windows.Application.Current.Resources[names[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        static double Linear(byte channel) { var value = channel / 255d; return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4); }
        var luminance = 0.2126 * Linear(accent.R) + 0.7152 * Linear(accent.G) + 0.0722 * Linear(accent.B);
        System.Windows.Application.Current.Resources["PrimaryTextBrush"] = (luminance + 0.05) / 0.05 >= 1.05 / (luminance + 0.05) ? Brushes.Black : Brushes.White;
        if (SystemParameters.HighContrast)
        {
            var contrast = new Brush[] { SystemColors.WindowBrush, SystemColors.WindowBrush, SystemColors.WindowTextBrush, SystemColors.GrayTextBrush, SystemColors.WindowTextBrush, SystemColors.HighlightBrush, SystemColors.ControlBrush, SystemColors.WindowTextBrush };
            for (var i = 0; i < names.Length; i++) System.Windows.Application.Current.Resources[names[i]] = contrast[i];
            System.Windows.Application.Current.Resources["PrimaryTextBrush"] = SystemColors.HighlightTextBrush;
        }
        backdrop.Apply(settings.WindowMaterial, dark);
        if (model.Page == "Liquid Glass preview") liquidGlassPreview?.ApplyAppearance(dark);
    }
    private static bool UsesDarkAppearance(string theme) => theme == "Dark" || (theme == "System" && Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0);
    private sealed record WindowMaterialChoice(string Key, string Label);
    private void WindowsAppearanceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (closing || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() => { if (!closing) ApplyTheme(appliedTheme); });
    }
    private async void AddEvent(OperationEvent ev)
    {
        ev = ev with { Text = Redactor.Redact(ev.Text) };
        events.Add(ev); if (events.Count > 10000) { events.RemoveRange(0, 1000); viewClearAt = Math.Max(0, viewClearAt - 1000); outputTruncated = true; }
        outputDirty = true; if (ev.State is OperationState.Failed or OperationState.Denied or OperationState.OutcomeUnknown) ShowProblem(ev.Kind.ToString(), ev.Text, ev.ProjectId);
        if (logStore is not null)
        {
            try { await logStore.AppendAsync(workspace?.Projects.FirstOrDefault(p => p.Id == ev.ProjectId)?.DisplayName ?? ev.ProjectId.ToString(), ev.OperationId + " " + ev.Kind + " " + ev.Stream, ev.State.ToString(), ev.Text); }
            catch (Exception error) { Banner("Local log persistence failed: " + error.Message); }
        }
    }
    private void RenderOutput()
    {
        if (OutputText is null) return;
        var filter = OutputSearch?.Text ?? ""; var selection = OutputChannel?.SelectedIndex ?? 0;
        var severity = OutputSeverity?.SelectedIndex ?? 0;
        var filtered = events.Skip(viewClearAt).Where(ev => selection == 3 || ev.ProjectId == model.Snapshot?.ProjectId).Where(ev => selection != 1 || ev.Kind is not (OperationKind.Fetch or OperationKind.GitHubRead or OperationKind.GitHubWrite)).Where(ev => selection != 2 || ev.Kind == OperationKind.Fetch).Where(ev => selection != 4 || ev.Kind is OperationKind.GitHubRead or OperationKind.GitHubWrite).Where(ev => filter.Length == 0 || ev.Text.Contains(filter, StringComparison.OrdinalIgnoreCase)).Where(ev => severity switch { 1 => ev.State is OperationState.Failed or OperationState.Denied or OperationState.OutcomeUnknown or OperationState.Stale, 2 => ev.State == OperationState.Succeeded, 3 => ev.State is OperationState.Queued or OperationState.Starting or OperationState.Running, _ => true });
        var builder = new StringBuilder(); Guid? last = null;
        if (outputTruncated) builder.AppendLine("[Earlier visible events were truncated. Retained redacted log files may contain more detail.]");
        foreach (var ev in filtered)
        {
            if (last != ev.OperationId) { builder.AppendLine($"\n── {EventPurpose(ev)} • {workspace?.Projects.FirstOrDefault(p => p.Id == ev.ProjectId)?.DisplayName ?? "project"} • operation {Short(ev.OperationId.ToString())} ──"); last = ev.OperationId; }
            builder.AppendLine($"[{ev.Time.ToLocalTime():HH:mm:ss}] {ev.State} / {ev.Stream}: {ev.Text}");
            if (builder.Length > 300000) { builder.Remove(0, builder.Length - 250000); builder.Insert(0, "[Earlier visible output was truncated. Use filters or retained local logs.]\n"); }
        }
        OutputText.Text = builder.Length == 0 ? "Waiting for app-managed activity. Terminal sessions have their own separate output." : builder.ToString();
        RenderOperationGroups(filtered.ToArray());
        if (AutoScroll?.IsChecked == true) OutputText.ScrollToEnd();
    }
    private void RenderOperationGroups(OperationEvent[] filtered)
    {
        if (OutputGroups is null) return;
        var groups = filtered.GroupBy(ev => ev.OperationId).TakeLast(75).ToArray();
        var visibleIds = groups.Select(group => group.Key).ToHashSet();
        foreach (var id in outputGroups.Keys.Where(id => !visibleIds.Contains(id)).ToArray()) outputGroups.Remove(id);
        OutputGroups.Children.Clear();
        if (groups.Length == 0) { Text(OutputGroups, "No activity matches these filters. Terminal output stays in its own session.", true, 12); return; }
        if (filtered.Select(ev => ev.OperationId).Distinct().Count() > 75) Text(OutputGroups, "Showing the latest 75 operation groups. Narrow the filters or use combined text for more retained output.", true, 12);
        foreach (var group in groups)
        {
            var first = group.First(); var last = group.Last(); var purpose = EventPurpose(first); var project = workspace?.Projects.FirstOrDefault(p => p.Id == first.ProjectId)?.DisplayName ?? "project";
            if (!outputGroups.TryGetValue(group.Key, out var entry))
            {
                var content = new StackPanel(); var text = Mono("", 130); AutomationProperties.SetName(text, purpose + " retained operation output");
                content.Children.Add(Action("Copy this operation", () =>
                {
                    Clipboard.SetText(BoundedOperationText(events.Skip(viewClearAt).Where(ev => ev.OperationId == group.Key), 250000)); Banner("Copied this operation's retained redacted output (bounded to 250,000 characters).");
                })); content.Children.Add(text);
                var expander = new Expander { Content = content, IsExpanded = group.Key == groups[^1].Key && last.State is OperationState.Starting or OperationState.Running, Margin = new(0, 0, 0, 5) };
                entry = (expander, text); outputGroups[group.Key] = entry;
            }
            entry.Expander.Header = $"{purpose} • {project} • {last.State} • {first.Time.ToLocalTime():HH:mm:ss} • {Short(group.Key.ToString())}";
            AutomationProperties.SetName(entry.Expander, entry.Expander.Header.ToString());
            var display = BoundedOperationText(group, 30000);
            entry.Text.Height = Math.Clamp((display.Count(character => character == '\n') + 1) * 18 + 20, 55, 130);
            if (entry.Text.Text != display)
            {
                var selectionStart = entry.Text.SelectionStart; var selectionLength = entry.Text.SelectionLength;
                entry.Text.Text = display; entry.Text.Select(Math.Min(selectionStart, display.Length), Math.Min(selectionLength, Math.Max(0, display.Length - selectionStart)));
                if (AutoScroll?.IsChecked == true && selectionLength == 0) entry.Text.ScrollToEnd();
            }
            OutputGroups.Children.Add(entry.Expander);
        }
        if (AutoScroll?.IsChecked == true) OutputGroupsScroll.ScrollToEnd();
    }
    private static string BoundedOperationText(IEnumerable<OperationEvent> operationEvents, int limit)
    {
        var lines = new List<string>(); var retained = 0; var truncated = false;
        foreach (var ev in operationEvents.Reverse())
        {
            var line = $"[{ev.Time.ToLocalTime():HH:mm:ss}] {ev.State} / {ev.Stream}: {ev.Text}";
            if (retained + line.Length + Environment.NewLine.Length > limit) { truncated = true; if (lines.Count == 0) lines.Add(line[^Math.Min(line.Length, limit)..]); break; }
            lines.Add(line); retained += line.Length + Environment.NewLine.Length;
        }
        lines.Reverse(); return (truncated ? "[Earlier operation output omitted. Retained local logs may contain more detail.]" + Environment.NewLine : "") + string.Join(Environment.NewLine, lines);
    }
    private static string EventPurpose(OperationEvent ev) => ev.Stream == "inspection" ? "Read project status" : ev.Stream == "hosted-purpose" ? ev.Text : ev.Kind.ToString();
    private void OutputGroupingChanged(object sender, RoutedEventArgs e)
    {
        if (OutputText is null || OutputGroupsScroll is null) return;
        OutputGroupsScroll.Visibility = GroupOutput.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; OutputText.Visibility = GroupOutput.IsChecked == true ? Visibility.Collapsed : Visibility.Visible; RenderOutput();
    }
    private void OutputFilterChanged(object sender, EventArgs e) => RenderOutput();
    private void CopyOutput(object sender, RoutedEventArgs e) { try { var selectedGroup = outputGroups.Values.FirstOrDefault(group => group.Text.IsKeyboardFocusWithin).Text; Clipboard.SetText(selectedGroup?.SelectedText.Length > 0 ? selectedGroup.SelectedText : OutputText.SelectedText.Length > 0 ? OutputText.SelectedText : OutputText.Text); } catch (Exception error) { Banner(error.Message); } }
    private void ClearOutput(object sender, RoutedEventArgs e) { viewClearAt = events.Count; RenderOutput(); }
    private void ShowProblem(string title, string detail, Guid? projectId = null)
    {
        var origin = projectId ?? model.Snapshot?.ProjectId; var projectName = origin.HasValue ? workspace?.Projects.FirstOrDefault(project => project.Id == origin)?.DisplayName : null;
        var attributedTitle = projectName is null ? title : projectName + " • " + title;
        Banner(attributedTitle + ": " + detail); var panel = new StackPanel { Margin = new(0, 0, 0, 16) }; Text(panel, attributedTitle, false, 15, FontWeights.SemiBold); Text(panel, detail, true);
        var friendly = GitErrorExplainer.Explain(detail) ?? GitErrorExplainer.Fallback; Text(panel, "In plain words · " + friendly.Title, false, 14, FontWeights.SemiBold); Text(panel, "What happened: " + friendly.WhatHappened, true); Text(panel, "What to do: " + friendly.WhatToDo, true); if (friendly.DocUrl is { } doc) panel.Children.Add(DocChip(doc));
        if (detail.Contains("author name and email", StringComparison.OrdinalIgnoreCase)) panel.Children.Add(Action("Set author details", () => Navigate("Author setup"), true));
        panel.Children.Add(Action("See full details", async () => await ReviewProblemContextAsync(origin, false))); panel.Children.Add(Action("Get help: make a summary to share", async () => await ReviewProblemContextAsync(origin, true))); ProblemsPanel.Children.Insert(0, panel); while (ProblemsPanel.Children.Count > 40) ProblemsPanel.Children.RemoveAt(40);
        SetActivityDock(true); DockTabs.SelectedIndex = 1;
    }
    private async Task ReviewProblemContextAsync(Guid? origin, bool support)
    {
        if (origin is Guid id && model.Snapshot?.ProjectId != id && workspace?.Projects.FirstOrDefault(project => project.Id == id) is { } project) await OpenProjectAsync(project.SelectedFolder);
        if (origin.HasValue && model.Snapshot?.ProjectId != origin) { Banner("Open the problem's originating project after current activity finishes."); return; }
        if (support) PrepareSupport(this, new RoutedEventArgs()); else { SetActivityDock(true); DockTabs.SelectedIndex = 0; }
    }
    private void Banner(string text) { StatusBanner.Visibility = Visibility.Visible; BannerText.Text = text; if (OpenAffectedProjectButton is not null) OpenAffectedProjectButton.Visibility = Visibility.Collapsed; }
    private async void OpenAffectedProject(object sender, RoutedEventArgs e)
    {
        if (workspace is not null && OpenAffectedProjectButton.Tag is Guid id && workspace.Projects.FirstOrDefault(project => project.Id == id) is { } project) await OpenProjectAsync(project.SelectedFolder);
    }
    private void PrepareSupport(object sender, RoutedEventArgs e)
    {
        var snapshot = model.Snapshot; if (snapshot is null || workspace is null) { Banner("Open a project before preparing its support context."); return; }
        var selected = events.Where(ev => ev.ProjectId == snapshot.ProjectId).TakeLast(200).Select(ev => $"{ev.Time:O} {ev.OperationId} {ev.Kind} {ev.State} {ev.Stream} {ev.Text}");
        var bundle = diagnosticExporter.Prepare(snapshot.Project.DisplayName, snapshot.Branch ?? snapshot.BranchState.ToString(), AccessText(workspace.Access.Get(snapshot.ProjectId).Level), snapshot.GitVersion, snapshot.Remote.LastSuccessfulCheck, selected);
        reviewing = true; PageContent.Children.Clear(); Heading("Review support information", "Review this redacted local summary before saving or taking a screenshot. Source diffs and terminal scrollback are excluded.");
        var context = $"Project: {snapshot.Project.DisplayName}\nBranch: {snapshot.Branch ?? snapshot.BranchState.ToString()}\nAccess: {AccessText(workspace.Access.Get(snapshot.ProjectId).Level)}\nUpstream: {snapshot.Upstream ?? "not configured"}\nPush destination: {snapshot.Push?.Remote ?? "unknown"} / {snapshot.Push?.DestinationRef ?? "unknown"}\nLast successful remote check: {snapshot.Remote.LastSuccessfulCheck?.ToLocalTime().ToString("g") ?? "not checked"}";
        AddCard(PageContent, Redactor.Redact(context)); Text(PageContent, "Readable summary — exactly what will be saved as summary.md"); var review = Mono(bundle.Markdown, 300); review.IsReadOnly = false; PageContent.Children.Add(review);
        Text(PageContent, "Structured events — exactly what will be saved as events.json"); var jsonReview = Mono(bundle.Json, 250); jsonReview.IsReadOnly = false; PageContent.Children.Add(jsonReview);
        Text(PageContent, "Redaction is best-effort. Remove any remaining private information. Saving does not send a report.", true);
        PageContent.Children.Add(Action("Save reviewed diagnostic bundle", async () =>
        {
            var picker = new OpenFolderDialog { Title = "Choose a local folder for the reviewed support bundle" };
            if (picker.ShowDialog(this) == true) { using var validated = System.Text.Json.JsonDocument.Parse(jsonReview.Text); await diagnosticExporter.SaveAsync(bundle with { Markdown = review.Text, Json = jsonReview.Text }, picker.FolderName); Banner("Reviewed support bundle saved locally."); }
        }, true)); PageContent.Children.Add(Action("Copy reviewed summary", () => Clipboard.SetText(review.Text))); PageContent.Children.Add(Action("Back to project", () => Navigate("Overview")));
    }
    private async void NewTerminal(object sender, RoutedEventArgs e)
    {
        if (workspace is null || model.Snapshot is null) return;
        await WorkAsync(async () =>
        {
            var snapshot = await workspace.RefreshAsync(model.Snapshot.ProjectId); model.Snapshot = snapshot;
            if (snapshot.Project.Kind is RepositoryKind.Invalid or RepositoryKind.Bare) throw new InvalidOperationException("An interactive shell needs a valid reviewed working project folder. Reopen the project and resolve its setup first.");
            if (snapshot.Project.Kind == RepositoryKind.Candidate && !string.Equals(snapshot.Project.RootPath, Path.GetFullPath(snapshot.Project.SelectedFolder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The selected candidate folder changed identity. Reopen it before starting a shell.");
            workspace.Access.Authorize(snapshot.ProjectId, OperationKind.Terminal);
            if (!snapshot.Project.Trusted) throw new AccessDeniedException("Enable trusted project access before creating a shell.");
            var session = await new ConPtyTerminalHost().CreateAsync(snapshot.ProjectId.ToString(), snapshot.Project.RootPath, "PowerShell " + (sessions.Count + 1) + " — " + snapshot.Project.DisplayName, new AccessRegistryTerminalGate(workspace.Access));
            var view = new TerminalView(session); var tab = new TabItem { Header = session.DisplayName, Content = view, ToolTip = "Starting project: " + session.ProjectRoot };
            sessions.Add((session, view, tab)); TerminalTabs.Items.Add(tab); TerminalTabs.SelectedItem = tab;
            view.HostFailure += (_, error) => Dispatcher.BeginInvoke(() => ShowProblem("Terminal renderer", error));
            try { await view.InitializeAsync(); } catch { await view.DisposeAsync(); TerminalTabs.Items.Remove(tab); sessions.RemoveAll(s => s.Session.Id == session.Id); throw; }
            DockTabs.SelectedIndex = 2;
            SetActivityDock(true);
        });
    }
    private TerminalView? SelectedTerminal() => (TerminalTabs.SelectedItem as TabItem)?.Content as TerminalView;
    private async void InterruptTerminal(object sender, RoutedEventArgs e) { try { if (SelectedTerminal() is { } view) await view.InterruptAsync(); } catch (Exception error) { ShowProblem("Terminal interrupt", error.Message); } }
    private async void PasteTerminal(object sender, RoutedEventArgs e) { try { if (SelectedTerminal() is { } view && Clipboard.ContainsText()) await view.PasteAsync(Clipboard.GetText()); } catch (Exception error) { ShowProblem("Terminal paste", error.Message); } }
    private async void CopyTerminal(object sender, RoutedEventArgs e) { try { if (SelectedTerminal() is { } view) await view.CopySelectionAsync(); } catch (Exception error) { ShowProblem("Terminal copy", error.Message); } }
    private async void CloseTerminal(object sender, RoutedEventArgs e)
    {
        if (TerminalTabs.SelectedItem is not TabItem tab) return;
        var entry = sessions.First(s => s.Tab == tab);
        if (!entry.Session.IsClosed && MessageBox.Show(this, "Close this session and its owned child processes? Unfinished shell activity will stop.", "Close terminal", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        await entry.View.DisposeAsync(); TerminalTabs.Items.Remove(tab); sessions.Remove(entry); await RefreshAsync(false);
    }
    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (closeCleanupComplete) return;
        if (closing) { e.Cancel = true; return; }
        if (sessions.Any(s => !s.Session.IsClosed) || model.Busy)
        {
            if (MessageBox.Show(this, "Exit GitCompass, stop monitoring, cancel its operations, and close owned terminal sessions?", "Exit GitCompass", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) { e.Cancel = true; return; }
        }
        e.Cancel = true; closing = true; SystemEvents.UserPreferenceChanged -= WindowsAppearanceChanged; timer.Stop(); outputTimer.Stop(); activeCancellation?.Cancel(); authenticationCancellation?.Cancel();
        if (scheduler is not null) await scheduler.DisposeAsync();
        foreach (var entry in sessions.ToArray()) await entry.View.DisposeAsync(); http.Dispose();
        // Cleanup can finish synchronously (for example, before Git is installed).
        // WPF must finish the canceled Closing event before another Close is requested.
        _ = Dispatcher.BeginInvoke(() => { closeCleanupComplete = true; Close(); });
    }
    private Button OperationButton(string title, OperationKind kind, Action action)
    {
        var button = Action(title, action, false); var permitted = model.Snapshot is not null && workspace is not null && Permission(model.Snapshot.ProjectId, kind);
        button.IsEnabled = permitted; button.ToolTip = permitted ? "Review the exact effects before execution." : "Requires " + AccessText(AccessRegistry.Required(kind)) + ". Select the project Access mode to enable this capability."; ToolTipService.SetShowOnDisabled(button, true); return button;
    }
    private Button OperationButton(string title, OperationKind kind, Func<Task> action) => OperationButton(title, kind, (Action)(async () => { try { await action(); } catch (Exception e) { ShowProblem(title, e.Message); } }));
    private static string FileDescription(FileState file)
    {
        var codes = new[] { file.IndexStatus, file.WorktreeStatus };
        var change = file.Kind == FileKind.Conflicted ? "Conflict — resolution needed" : file.Kind == FileKind.Untracked ? "New file" : codes.Contains('D') ? "Deleted" : codes.Contains('R') ? "Renamed" : codes.Contains('A') ? "Added" : codes.Contains('T') ? "File type changed" : "Modified";
        var selection = file.Kind == FileKind.Conflicted ? "" : file.IsStaged ? (file.IsUnstaged ? "Selected for commit • more edits not selected" : "Selected for commit") : "Not selected for commit";
        return file.Path + (file.OriginalPath is null ? "" : " ← " + file.OriginalPath) + "  — " + change + (selection.Length == 0 ? "" : " • " + selection);
    }
    private static string DescribeRefs(ImmutableArray<string> refs) => string.Join(" • ", refs.Where(r => !r.StartsWith("refs/remotes/", StringComparison.Ordinal) || !r.EndsWith("/HEAD", StringComparison.Ordinal)).Select(r => r.StartsWith("HEAD -> refs/heads/", StringComparison.Ordinal) ? "Your branch: " + r[19..] : r.StartsWith("refs/heads/", StringComparison.Ordinal) ? "Local branch: " + r[11..] : r.StartsWith("refs/remotes/", StringComparison.Ordinal) ? "Fetched copy: " + r[13..] : r.StartsWith("refs/tags/", StringComparison.Ordinal) ? "Tag: " + r[10..] : r == "HEAD" ? "Current version (HEAD)" : r));
    private static string AccessText(AccessLevel level) => level switch { AccessLevel.ReadOnly => "Read-only", AccessLevel.MonitorOnly => "Monitor only", _ => "Allow changes" };
    private static string Short(string value) => value.Length <= 8 ? value : value[..8];
    private void Heading(string title, string subtitle)
    {
        PageBreadcrumb.Text = model.Snapshot is null ? "GitCompass  ›  " + PageName(model.Page) : "Projects  ›  " + model.Snapshot.Project.DisplayName + "  ›  " + PageName(model.Page);
        Text(PageContent, title, false, 28, FontWeights.SemiBold); Text(PageContent, subtitle, true, 14);
        if (GitGlossary.TermsIn(title + " " + subtitle).Count > 0)
        {
            var help = Action(WhatsThisLabel, () => ShowWhatsThis(GitGlossary.TermsIn(title + " " + subtitle)));
            help.Padding = new(10, 4, 10, 4); help.MinHeight = 30; help.FontSize = 13;
            AutomationProperties.SetHelpText(help, "Explains the Git words on this page in everyday language, with links to the official docs. Shortcut: F1.");
            PageContent.Children.Add(help);
        }
    }
    public const string WhatsThisLabel = "? What do these words mean (F1)";
    private bool whatsThisHooked;
    /// <summary>F1 anywhere: explain the Git words in the focused control (button, text, field label), falling back to the page heading.</summary>
    private void HookWhatsThis()
    {
        if (whatsThisHooked) return; whatsThisHooked = true;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.F1) return;
            e.Handled = true;
            var focused = System.Windows.Input.Keyboard.FocusedElement as DependencyObject;
            var terms = GitGlossary.TermsIn(DescribeForHelp(focused));
            if (terms.Count == 0) terms = GitGlossary.TermsIn(string.Join(" ", PageContent.Children.OfType<TextBlock>().Take(2).Select(t => t.Text)));
            ShowWhatsThis(terms);
        };
    }
    private static string DescribeForHelp(DependencyObject? element) => element switch
    {
        null => "",
        TextBlock block => block.Text,
        ContentControl { Content: string text } control => text + " " + AutomationProperties.GetName(control),
        FrameworkElement other => AutomationProperties.GetName(other) + " " + AutomationProperties.GetHelpText(other) + " " + (other.ToolTip as string ?? ""),
        _ => "",
    };
    private void ShowWhatsThis(IReadOnlyList<GlossaryTerm> terms)
    {
        var dialog = new WhatsThisDialog(this, terms.Take(6).ToList(), id => learning?.Find(id)?.Title);
        var returnFocus = System.Windows.Input.Keyboard.FocusedElement;
        var result = dialog.ShowDialog();
        if (result == true && dialog.ReplayRequested) { _ = ReplayWalkthroughSafeAsync(); return; }
        if (result == true && dialog.LessonToOpen is { } lesson) ShowLesson(lesson);
        else returnFocus?.Focus();
    }
    private async Task ReplayWalkthroughSafeAsync() { try { await ReplayWalkthroughAsync(); } catch (Exception e) { ShowProblem("Replay walkthrough", e.Message); } }
    private Button DocChip(string url) => DocLinks.Chip(url, problem => Banner(problem));
    private static void Subheading(Panel panel, string title) => Text(panel, title, false, 18, FontWeights.SemiBold);
    private static void Text(Panel panel, string value, bool muted = false, double size = 14, FontWeight? weight = null)
    {
        var text = new TextBlock { Text = value, FontSize = size, FontWeight = weight ?? FontWeights.Normal, Margin = new(0, 6, 0, 10), TextWrapping = TextWrapping.Wrap }; text.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "TextBrush"); panel.Children.Add(text);
    }
    private static TextBox Input(Panel panel, string label, string initial, bool multiline = false)
    {
        Text(panel, label); var box = new TextBox { Text = initial, Margin = new(0, 0, 0, 10), MinHeight = multiline ? 80 : 35, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap }; AutomationProperties.SetName(box, label); panel.Children.Add(box); return box;
    }
    private static TextBox Mono(string text, double height) => new() { Text = text, IsReadOnly = true, FontFamily = new("Cascadia Mono, Consolas"), FontSize = 12, Height = height, Margin = new(0, 10, 0, 10), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap };
    private static WrapPanel Row() => new() { Margin = new(0, 8, 0, 12) };
    private static Grid Columns(int count)
    {
        return new AdaptiveColumnsGrid(count, count == 3 ? 640 : 700) { Margin = new(0, 0, 0, 16) };
    }
    private static Border Card(object content)
    {
        var border = new Border { Padding = new(20, 16, 20, 16), Margin = new(0, 4, 10, 8), BorderThickness = new(1), CornerRadius = new(4) }; border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        border.Child = content as UIElement ?? new TextBlock { Text = content.ToString(), TextWrapping = TextWrapping.Wrap, LineHeight = 22 }; return border;
    }
    private static void AddCard(Panel panel, object content) => panel.Children.Add(Card(content));
    private static Border SettingsRow(string title, string description, FrameworkElement action)
    {
        var text = new StackPanel(); Text(text, title, false, 15, FontWeights.SemiBold); Text(text, description, true, 13);
        return Card(new AdaptiveSettingsGrid(text, action));
    }
    private static void AddCard(Grid grid, object content, int column) { var card = Card(content); Grid.SetColumn(card, column); grid.Children.Add(card); }
    private static FrameworkElement Stat(string number, string label, string explanation, Action action)
    {
        var panel = new StackPanel(); Text(panel, number, false, 30, FontWeights.SemiBold); Text(panel, label, false, 17, FontWeights.SemiBold); Text(panel, explanation, true, 12);
        foreach (TextBlock text in panel.Children) text.Margin = new(0, 0, 0, 6);
        var button = Action("", action); button.Content = panel; button.Padding = new(0); button.BorderThickness = new(0); button.Background = Brushes.Transparent; button.Margin = new(0); button.HorizontalContentAlignment = HorizontalAlignment.Left; AutomationProperties.SetName(button, number + " " + label + ". " + explanation + " Review details."); return button;
    }
    private static Button Action(string title, Action action, bool primary = false)
    {
        var button = new Button { Content = title, MaxWidth = 520, Margin = new(0, 4, 8, 8), HorizontalAlignment = HorizontalAlignment.Left }; if (primary) button.SetResourceReference(StyleProperty, "PrimaryButton"); button.Click += (_, _) => action(); return button;
    }
    private Button Action(string title, Func<Task> action, bool primary = false) => Action(title, (Action)(async () => { try { await action(); } catch (Exception e) { ShowProblem(title, e.Message); } }), primary);
    private void OpenLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") { Banner("Only reviewed HTTPS links can be opened here."); return; }
        try { Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true }); } catch (Exception e) { ShowProblem("Open browser", e.Message); }
    }
}
