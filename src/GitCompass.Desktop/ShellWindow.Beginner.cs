using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using GitCompass.Domain;
using GitCompass.Infrastructure.Git;
using GitCompass.Infrastructure.Local;
using Microsoft.Win32;

namespace GitCompass.Desktop;

public partial class ShellWindow
{
    private BeginnerGuideState beginner = new();
    private bool beginnerStateReadable = true;
    private string teamProjectLink = "";

    private async Task InitializeBeginnerAsync()
    {
        try { beginner = await store.LoadBeginnerGuideAsync(); }
        catch (Exception error)
        {
            beginnerStateReadable = false;
            setupError = (setupError is null ? "" : setupError + "\n") + "Your walkthrough could not be resumed. Its saved record has been preserved. " + error.Message;
        }
    }

    private async Task SaveBeginnerAsync()
    {
        // Preserve a damaged record rather than silently replacing it with defaults.
        if (!beginnerStateReadable) return;
        try { await store.SaveBeginnerGuideAsync(beginner); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            beginnerStateReadable = false;
            Banner("Walkthrough progress could not be saved for next time. Your project operation is separate. " + error.Message);
        }
    }

    private bool IsGuideProject => model.Snapshot is not null && SameFolder(beginner.ProjectFolder, model.Snapshot.Project.SelectedFolder);
    private static bool SameFolder(string? left, string? right) => left is not null && right is not null &&
        string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private async Task OnBeginnerProjectOpenedAsync()
    {
        RestorePracticeAccess();
        if (!beginner.Active || model.Snapshot is null) return;
        if (!IsGuideProject)
            beginner = beginner with { ProjectFolder = model.Snapshot.Project.SelectedFolder, Intent = "", TeamCopyFolder = null, SavedCommitId = null };
        await SaveBeginnerAsync();
    }

    private async Task OnBeginnerOperationCompletedAsync(OperationKind kind, OperationState state)
    {
        if (!beginner.Active || !IsGuideProject || model.Snapshot is null || state != OperationState.Succeeded) return;
        if (IsPracticeProject)
        {
            if (BeginnerMissions.Clamp(beginner.MissionIndex) == BeginnerMissions.BranchMerge)
            {
                var step = BeginnerMissions.AdvanceBranchStep(beginner.BranchStep, kind, model.Snapshot.Branch, PracticeBase);
                if (step != beginner.BranchStep) { beginner = beginner with { BranchStep = step }; await SaveBeginnerAsync(); Banner("✓ Step done: " + BeginnerMissions.BranchSteps(PracticeBase)[step - 1] + ". Next: " + BeginnerMissions.BranchSteps(PracticeBase)[step] + "."); }
            }
            var next = BeginnerMissions.Advance(beginner.MissionIndex, kind, state);
            if (next != beginner.MissionIndex)
            {
                var completed = beginner.MissionIndex;
                beginner = beginner with { MissionIndex = next, SavedCommitId = kind == OperationKind.Commit ? model.Snapshot.HeadId : beginner.SavedCommitId };
                await SaveBeginnerAsync();
                CelebrateMission(completed, next);
            }
            return;
        }
        if (kind == OperationKind.Commit && model.Snapshot.HeadId is not null)
        {
            beginner = beginner with { SavedCommitId = model.Snapshot.HeadId };
            await SaveBeginnerAsync();
            Banner("Your version is saved on this computer. This step has not uploaded it. You can return to the walkthrough to review your progress.");
        }
        // A clone brings existing history. It never counts as the user's first local save.
        if (kind == OperationKind.Clone)
        {
            beginner = beginner with { SavedCommitId = null };
            await SaveBeginnerAsync();
        }
    }

    private async Task StartBeginnerAsync()
    {
        var folder = model.Snapshot?.Project.SelectedFolder;
        beginner = beginner with { Active = true, WelcomeDismissed = true };
        if (folder is not null && !SameFolder(beginner.ProjectFolder, folder))
            beginner = beginner with { ProjectFolder = folder, Intent = "", TeamCopyFolder = null, SavedCommitId = null };
        await SaveBeginnerAsync();
        Navigate("Walkthrough");
    }

    private async Task ResumeBeginnerAsync()
    {
        beginner = beginner with { Active = true, WelcomeDismissed = true };
        if (beginner.ProjectFolder is not null && !Directory.Exists(beginner.ProjectFolder))
        {
            beginner = beginner with { ProjectFolder = model.Snapshot?.Project.SelectedFolder, Intent = "", TeamCopyFolder = null, SavedCommitId = null };
            Banner("The previous walkthrough folder is no longer available. Choose its new location or continue with your current project.");
        }
        await SaveBeginnerAsync();
        if (beginner.ProjectFolder is not null && !IsGuideProject && Directory.Exists(beginner.ProjectFolder))
            await OpenProjectAsync(beginner.ProjectFolder);
        Navigate("Walkthrough");
    }

    private async Task SkipWithQuizAsync()
    {
        var dialog = new SkipQuizDialog(this);
        dialog.ShowDialog();
        switch (dialog.Outcome)
        {
            case SkipQuizOutcome.Cancelled:
                Navigate("Overview");
                Banner(QuizCancelledBanner);
                break;
            case SkipQuizOutcome.StartWalkthrough: await StartBeginnerAsync(); break;
            case SkipQuizOutcome.OpenProject:
                beginner = beginner with { Active = false, WelcomeDismissed = true };
                await SaveBeginnerAsync();
                PickFolder(this, new RoutedEventArgs());
                break;
            case SkipQuizOutcome.CloneRepository:
                beginner = beginner with { WelcomeDismissed = true };
                await SaveBeginnerAsync();
                await ChooseCloneDestinationAsync();
                break;
        }
    }

    /// <summary>Called by the app at startup (not by tests): shows the welcome screen once, before any walkthrough.</summary>
    public async Task ShowWelcomeIfFirstLaunchAsync()
    {
        if (!beginnerStateReadable || beginner.WelcomeSeen) return;
        if (!await EnsureGitBeforeWelcomeAsync()) return; // welcome waits until Git is installed (shown again next launch)
        await ShowWelcomeAsync();
    }

    /// <summary>Git check before the welcome picker: one plain install step with Download and "check again".</summary>
    private async Task<bool> EnsureGitBeforeWelcomeAsync()
    {
        var stillMissing = false;
        while (workspace is null)
        {
            var dialog = new GitSetupDialog(this, stillMissing);
            if (dialog.ShowDialog() != true || !dialog.CheckAgain) return false;
            await RecheckGitAsync();
            stillMissing = true;
        }
        return true;
    }

    private async Task ShowWelcomeAsync()
    {
        var dialog = new WelcomeDialog(this);
        dialog.ShowDialog();
        beginner = beginner with { WelcomeSeen = true };
        if (dialog.Choice is not { } level) { await SaveBeginnerAsync(); return; }
        await ApplyWelcomeChoiceAsync(level);
    }

    /// <summary>Picker routing: brand new → full walkthrough with missions; a little → key steps only; knows Git → the skip quiz.</summary>
    public async Task ApplyWelcomeChoiceAsync(ExperienceLevel level)
    {
        beginner = beginner with { WelcomeSeen = true, Experience = Welcome.ToState(level) };
        switch (Welcome.Route(level))
        {
            case WelcomeRoute.SkipQuiz: await SaveBeginnerAsync(); await SkipWithQuizAsync(); break;
            default: await StartBeginnerAsync(); break;
        }
    }

    public BeginnerGuideState BeginnerState => beginner;

    private async Task ReplayWalkthroughAsync()
    {
        beginner = beginner with { MissionIndex = beginner.GuidedPractice ? BeginnerMissions.SeeChange : beginner.MissionIndex, SavedCommitId = null, PushOffered = false };
        await StartBeginnerAsync();
    }

    private async Task PauseBeginnerAsync()
    {
        beginner = beginner with { Active = false, WelcomeDismissed = true };
        await SaveBeginnerAsync();
        Navigate("Overview");
    }

    /// <summary>Exit asks first; Pause leaves immediately. Both keep MissionIndex, so Resume returns to the same mission.</summary>
    private async Task ExitBeginnerAsync()
    {
        var index = BeginnerMissions.Clamp(beginner.MissionIndex);
        var label = IsPracticeProject && index < BeginnerMissions.Done ? $"mission {index + 1} of {BeginnerMissions.Count}" : null;
        var dialog = new LeaveWalkthroughDialog(this, label);
        if (dialog.ShowDialog() == true && dialog.Leave) await PauseBeginnerAsync();
    }

    private void BuildBeginnerWelcome()
    {
        Heading("Open your project", "Choose any project folder. GitCompass will check whether it already has version history.");
        var open = new StackPanel();
        Subheading(open, "Your project, your files");
        Text(open, "Git remembers versions on your computer. GitHub is an optional online place to share them. You can learn and save locally without an account.", true);
        open.Children.Add(Action("Select project folder", () => PickFolder(this, new RoutedEventArgs()), true));
        Text(open, "Opening a folder inspects it in Read-only. Your files are preserved.", true);
        AddCard(PageContent, open);

        var guide = new StackPanel();
        Subheading(guide, beginner.Active ? "Continue your walkthrough" : "New to Git? Start here");
        Text(guide, "Choose a folder, understand its history, select your changes, and save a version on this computer. You can pause and return at any time.", true);
        guide.Children.Add(Action(beginner.Active || beginner.ProjectFolder is not null ? "Resume walkthrough" : "Start the walkthrough", beginner.ProjectFolder is null ? StartBeginnerAsync : ResumeBeginnerAsync, !beginner.WelcomeDismissed));
        guide.Children.Add(Action("Create guided practice project", CreateGuidedPracticeProjectAsync));
        if (beginner.Experience != "New") guide.Children.Add(Action(SampleFolderLabel, CreatePracticeProjectAsync));
        if (!beginner.WelcomeDismissed) guide.Children.Add(Action("Skip — I know Git", SkipWithQuizAsync));
        if (beginner.ProjectFolder is not null) Text(guide, "Walkthrough project: " + beginner.ProjectFolder, true);
        if (beginner.ProjectFolder is not null && !Directory.Exists(beginner.ProjectFolder)) Text(guide, "That folder is no longer available. Choose its new location or another folder to continue.", true);
        AddCard(PageContent, guide);

        if (setupError is not null)
        {
            AddCard(PageContent, setupError);
            PageContent.Children.Add(Action("Open setup settings", () => Navigate("Settings")));
            PageContent.Children.Add(Action("Read the offline introduction", () => ShowLesson("journey")));
        }
        Subheading(PageContent, "Recent projects");
        if (ProjectSelector.Items.Count == 0) Text(PageContent, "Your recently opened folders will appear here.", true);
        foreach (var recent in ProjectSelector.Items.OfType<RecentProject>())
        {
            var row = new StackPanel(); Text(row, recent.DisplayName, false, 16, FontWeights.SemiBold); Text(row, recent.Folder, true);
            row.Children.Add(Action("Open project", async () => await OpenProjectAsync(recent.Folder)));
            AddCard(PageContent, row);
        }
    }

    private void BuildGuideEntry(Panel target)
    {
        target.Children.Add(SettingsRow("First version walkthrough", beginner.Active && IsGuideProject ? "Continue your next step using this project's actual status." : "New to Git? Get help choosing files and saving a version on this computer.",
            Action(beginner.Active && IsGuideProject ? "Continue walkthrough" : "Guide me through a local save", StartBeginnerAsync)));
    }

    private void BuildReplayEntries(Panel target)
    {
        target.Children.Add(SettingsRow("Replay walkthrough", "Start the walkthrough again from the first step. Your files and saved versions are not changed.", Action("Replay walkthrough", ReplayWalkthroughAsync)));
        target.Children.Add(SettingsRow("Show welcome screen again", "Pick your experience level again: brand new, a little, or I know Git.", Action("Show welcome screen again", ShowWelcomeAsync)));
    }

    private async Task SetCandidateIntentAsync(string intent)
    {
        var snapshot = model.Snapshot;
        if (snapshot is null) return;
        beginner = beginner with { Active = true, WelcomeDismissed = true, ProjectFolder = snapshot.Project.SelectedFolder,
            Intent = intent, TeamCopyFolder = intent == "TeamCopy" ? snapshot.Project.SelectedFolder : null, SavedCommitId = null };
        await SaveBeginnerAsync();
        RenderPage();
    }

    private void BuildCandidateOverview()
    {
        var snapshot = model.Snapshot;
        if (snapshot is null) { BuildBeginnerWelcome(); return; }
        if (model.Page != "Walkthrough") Heading("Set up this project", "Choose how to start local version history or get your team's existing history.");
        var explanation = new StackPanel();
        Subheading(explanation, "This folder has no version history yet");
        Text(explanation, "Your files are still here. Choose where this project came from so we can suggest the right next step.", true);
        Text(explanation, "Folder: " + snapshot.Project.RootPath, true);
        var choices = Row();
        choices.Children.Add(Action("These are my own files", async () => await SetCandidateIntentAsync("OwnFiles")));
        choices.Children.Add(Action("I got a team copy or project link", async () => await SetCandidateIntentAsync("TeamCopy")));
        choices.Children.Add(Action("Just look around", PauseBeginnerAsync));
        explanation.Children.Add(choices); AddCard(PageContent, explanation);

        if (!IsGuideProject || beginner.Intent.Length == 0) return;
        if (beginner.Intent == "OwnFiles")
        {
            var start = new StackPanel();
            Subheading(start, "Start remembering versions of my files");
            Text(start, "This creates local Git history in this folder. Your files stay in place. Nothing is uploaded and no GitHub account is needed. The first branch will be called main; a branch is a named line of saved versions.", true);
            ExplainBeginnerAccess(start);
            start.Children.Add(OperationButton("Preview starting version saving here (git init)", OperationKind.Initialize, async () => await RequestAsync(OperationKind.Initialize, branch: "main")));
            AddCard(PageContent, start);
        }
        else BuildTeamCopySetup(PageContent);
    }

    private void BuildTeamCopySetup(Panel target)
    {
        var team = new StackPanel();
        Subheading(team, "Download the team's project history");
        Text(team, "A ZIP or Drive copy may have files without their saved history. Ask your teammate for the project's Git repository link. Downloading that history is called cloning.", true);
        Text(team, "Keep your original copy. Download into a separate empty folder, then compare and copy only the work you want to keep. Keep the old .git folder out of the new copy.", true);
        if (beginner.TeamCopyFolder is not null) Text(team, "Original copy: " + beginner.TeamCopyFolder, true);
        var link = Input(team, "Team project link", teamProjectLink);
        link.TextChanged += (_, _) => teamProjectLink = link.Text;
        team.Children.Add(Action("Choose a separate empty download folder", ChooseCloneDestinationAsync));
        var destination = model.Snapshot?.Project.RootPath;
        var destinationReady = destination is not null && !SameFolder(destination, beginner.TeamCopyFolder) && EmptyFolderAvailable(destination);
        if (destinationReady)
        {
            Text(team, "Download folder: " + destination, true);
            ExplainBeginnerAccess(team);
            team.Children.Add(OperationButton("Preview downloading the team project (clone)", OperationKind.Clone, async () =>
            {
                if (string.IsNullOrWhiteSpace(link.Text)) { Banner("Ask your team for a project link, then paste it here."); return; }
                await RequestAsync(OperationKind.Clone, remoteUrl: link.Text.Trim());
            }));
        }
        else Text(team, "Choose an empty folder different from your original copy before reviewing the download.", true);
        AddCard(target, team);
    }

    private static bool EmptyFolderAvailable(string folder)
    {
        try { return Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any(); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private async Task ChooseCloneDestinationAsync()
    {
        var picker = new OpenFolderDialog { Title = "Choose a separate empty folder for the team project", Multiselect = false };
        if (picker.ShowDialog(this) != true) return;
        if (SameFolder(picker.FolderName, beginner.TeamCopyFolder) || Directory.EnumerateFileSystemEntries(picker.FolderName).Any())
        { Banner("Choose a separate empty folder. Your original project copy must be kept."); return; }
        if (workspace is null) return;
        var destination = await workspace.OpenAsync(picker.FolderName);
        if (destination.Project.Kind != RepositoryKind.Candidate)
        { Banner("That folder belongs to an existing Git project. Choose an empty folder outside that project."); return; }
        beginner = beginner with { Active = true, ProjectFolder = picker.FolderName, Intent = "TeamCopy", SavedCommitId = null };
        await SaveBeginnerAsync();
        await OpenProjectAsync(picker.FolderName);
        Navigate("Walkthrough");
    }

    public const string PracticeAllowLabel = "Let GitCompass make changes here";
    public const string PracticeAllowExplanation = "This is your own practice folder, made by GitCompass. Allowing changes lets the missions save versions here. You still confirm each step.";

    /// <summary>Guided practice folders created by GitCompass remember the user's "Let GitCompass make changes here" choice across sessions.</summary>
    private void RestorePracticeAccess()
    {
        if (workspace is null || model.Snapshot is null || !IsPracticeProject || !beginner.PracticeChangesAllowed) return;
        if (!BeginnerMissions.PracticeMarkerMatches(beginner.PracticeToken, ReadPracticeMarker())) return; // not verifiably ours: ask again
        var id = model.Snapshot.ProjectId;
        if (workspace.Access.Get(id).Level == AccessLevel.AllowChanges) return;
        workspace.SetTrusted(id, true);
        workspace.Access.SetLevel(id, AccessLevel.AllowChanges);
    }

    private async Task AllowPracticeChangesAsync()
    {
        if (workspace is null || model.Snapshot is null || !IsPracticeProject) return;
        var token = Guid.NewGuid().ToString("N");
        try { await File.WriteAllTextAsync(PracticeMarkerPath!, token); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentNullException) { Banner("GitCompass couldn't mark this practice folder, so it will ask again next time. " + error.Message); token = ""; }
        beginner = beginner with { PracticeChangesAllowed = token.Length > 0, PracticeToken = token.Length > 0 ? token : null };
        await SaveBeginnerAsync();
        if (token.Length == 0) { workspace.SetTrusted(model.Snapshot.ProjectId, true); workspace.Access.SetLevel(model.Snapshot.ProjectId, AccessLevel.AllowChanges); await RefreshAsync(false); UpdateContext(); RenderPage(); return; }
        RestorePracticeAccess();
        await RefreshAsync(false);
        UpdateContext(); RenderPage();
    }

    public const string SampleFolderLabel = "Empty practice folder (no missions)";
    public const string QuizCancelledBanner = "No problem. To begin, choose Select project folder below. You can see the welcome screen again from Settings.";
    public const string OwnProjectAllowLabel = "Allow changes in this folder";
    private string? PracticeMarkerPath => model.Snapshot is null ? null : Path.Combine(model.Snapshot.Project.CommonGitDirectory, BeginnerMissions.PracticeMarkerFile);
    private string? ReadPracticeMarker() { try { return PracticeMarkerPath is { } p && File.Exists(p) ? File.ReadAllText(p) : null; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; } }
    private string PracticeBase => beginner.PracticeBaseBranch ?? "main";

    private void ExplainBeginnerAccess(Panel target)
    {
        if (model.Snapshot is null || workspace is null) return;
        var grant = workspace.Access.Get(model.Snapshot.ProjectId);
        if ((grant.Level != AccessLevel.AllowChanges || grant.PendingLevel is not null) && IsPracticeProject)
        {
            Text(target, PracticeAllowExplanation, true);
            target.Children.Add(Action(PracticeAllowLabel, AllowPracticeChangesAsync, true));
        }
        else if (grant.Level != AccessLevel.AllowChanges || grant.PendingLevel is not null)
        {
            Text(target, "GitCompass is only looking at this folder right now (Read-only). To save versions here, allow changes. You still confirm each step, and you can switch back any time in the \"Project access\" box at the top.", true);
            target.Children.Add(Action(OwnProjectAllowLabel, () => { AccessSelector.SelectedIndex = (int)AccessLevel.AllowChanges; }, true));
        }
        else Text(target, "GitCompass can make changes here. You still confirm each step before it runs.", true);
    }

    private void BuildWalkthrough()
    {
        Heading("Your first saved version", "A guided local save, using your project's actual status. You can pause and return later.");
        var top = Row(); top.Children.Add(Action("Pause walkthrough", PauseBeginnerAsync)); top.Children.Add(Action("Exit walkthrough", ExitBeginnerAsync)); top.Children.Add(Action("Choose another project", () => PickFolder(this, new RoutedEventArgs()))); PageContent.Children.Add(top);
        if (model.Snapshot is null || workspace is null)
        {
            var choose = new StackPanel(); Subheading(choose, "1. Choose a project");
            Text(choose, "Select any folder with your project files, whether it uses Git or not. Or practice with an example in a new folder.", true);
            var brandNew = beginner.Experience == "New";
            if (brandNew) Text(choose, "New to Git? The guided practice project has short missions that teach each step safely.", true);
            choose.Children.Add(Action("Select project folder", () => PickFolder(this, new RoutedEventArgs()), !brandNew));
            choose.Children.Add(Action("Create guided practice project", CreateGuidedPracticeProjectAsync, brandNew));
            if (!brandNew) choose.Children.Add(Action(SampleFolderLabel, CreatePracticeProjectAsync));
            AddCard(PageContent, choose);
            if (setupError is not null) { AddCard(PageContent, setupError); PageContent.Children.Add(Action("Open setup settings", () => Navigate("Settings"))); }
            return;
        }
        if (IsPracticeProject) { BuildMissions(); return; }
        var snapshot = model.Snapshot;
        var shortMode = Welcome.ShowsShortWalkthrough(beginner.Experience);
        if (shortMode)
        {
            var note = new StackPanel(); Text(note, "Short walkthrough: just the key steps. Finished steps are hidden.", true);
            note.Children.Add(Action("Show the full walkthrough", async () => { beginner = beginner with { Experience = "New" }; await SaveBeginnerAsync(); Navigate("Walkthrough"); }));
            AddCard(PageContent, note);
        }
        var nextActionIndex = PageContent.Children.Count;
        // This is a historical record of a successful reviewed save, independent of
        // whichever branch comparison or page of history happens to be displayed.
        var recorded = IsGuideProject && beginner.SavedCommitId is not null;
        var selected = new StackPanel(); Subheading(selected, "1. Project selected"); Text(selected, snapshot.Project.DisplayName + "\n" + snapshot.Project.RootPath, true);
        if (!SameFolder(snapshot.Project.SelectedFolder, snapshot.Project.RootPath))
        {
            Text(selected, "This folder is inside another project that already keeps saved versions. Anything you save here applies to that whole project, including files outside this folder.", true);
            selected.Children.Add(Action("Pick a different folder", () => PickFolder(this, new RoutedEventArgs())));
        }
        AddGuideSection(selected, "1. Project selected · " + snapshot.Project.DisplayName, true);
        if (snapshot.Project.Kind == RepositoryKind.Candidate) { BuildCandidateOverview(); return; }
        if (snapshot.Project.Kind is not (RepositoryKind.WorkingTree or RepositoryKind.LinkedWorktree))
        { AddCard(PageContent, "This folder cannot support a guided local save. Choose a normal project folder with working files. The project's reported problems are available on Home."); return; }

        var history = new StackPanel(); Subheading(history, "2. Local history is ready");
        Text(history, snapshot.HeadId is null ? "Git is set up. Your first saved version will start the history." : "This project already has saved versions. This walkthrough will help you save your next change.", true);
        if (beginner.Intent == "TeamCopy" && beginner.TeamCopyFolder is not null)
            Text(history, "Your original team copy remains at " + beginner.TeamCopyFolder + ". Compare it with this downloaded project and copy only the changes you want to keep. Downloading existing history does not count as saving your own change.", true);
        AddGuideSection(history, "2. Local history is ready", true);
        var grant = workspace.Access.Get(snapshot.ProjectId);
        var access = new StackPanel(); Subheading(access, "3. Review project access"); ExplainBeginnerAccess(access);
        AddGuideSection(access, "3. Project access · " + AccessText(grant.Level), grant.Level == AccessLevel.AllowChanges && grant.PendingLevel is null);
        var author = new StackPanel(); Subheading(author, "4. Identify your saved versions");
        if (snapshot.HasAuthorIdentity) Text(author, "Author ready: " + snapshot.AuthorName + " · " + snapshot.AuthorEmail + ". These details are recorded in saved versions and can be visible if you share them.", true);
        else Text(author, "Git needs your author name and email before saving. They identify your work; they do not sign you in to GitHub. Choose details you are comfortable sharing with collaborators.", true);
        author.Children.Add(Action(snapshot.HasAuthorIdentity ? "Review author details" : "Set author details", () => Navigate("Author setup")));
        AddGuideSection(author, snapshot.HasAuthorIdentity ? "4. Author details are ready" : "4. Set your author details", snapshot.HasAuthorIdentity);

        var files = new StackPanel(); Subheading(files, "5. Choose what goes into your version");
        Text(files, snapshot.Files.Length == 0 ? "There are no changed files yet. Make a small edit, save it in your editor, then refresh local status." : $"Git sees {snapshot.Files.Length} changed files. Open Changed files, inspect each file, choose its contents, then review your selection.", true);
        if (!shortMode) Text(files, "Staging means selecting contents for the next version. Saving includes all staged contents, including anything selected earlier. Review ignore suggestions for generated files and private information.", true);
        var stagedCount = snapshot.Files.Count(file => file.IsStaged);
        Text(files, $"Currently selected: {stagedCount} paths. Other edits stay in your working folder.", true);
        files.Children.Add(Action("Open Changed files", () => Navigate("Changes"), snapshot.HasAuthorIdentity));
        files.Children.Add(Action("Refresh local status", async () => await RefreshAsync()));
        AddGuideSection(files, recorded ? "5. Reviewed file contents were saved" : "5. Choose contents for your version", recorded);

        var save = new StackPanel();
        Subheading(save, recorded ? "6. Your version is saved on this computer" : "6. Save a version on this computer");
        Text(save, recorded ? (snapshot.HeadId == beginner.SavedCommitId ? "Saved version: " : "Earlier walkthrough save: ") + Short(beginner.SavedCommitId!) + ". " +
            (snapshot.HeadId == beginner.SavedCommitId ? "" : "The current branch now shows a different version. Inspect Saved versions to see its available history. ") +
            "Saving a version does not upload it. Online sharing is a separate reviewed action." : "On Changed files, describe what changed and choose Preview saving this version (commit). A commit is a saved version. Check the project and all selected contents, then run the reviewed operation.", true);
        if (recorded)
        {
            save.Children.Add(Action("View saved versions", () => Navigate("History graph")));
            save.Children.Add(Action("Finish walkthrough", PauseBeginnerAsync, true));
            Text(save, snapshot.HasRemotes ? "This project has an online connection. Checking updates and uploading need their own permission and review." : "No online connection is configured. You can keep working locally. Use Sharing when you have an authorized project link.", true);
        }
        else if (snapshot.HasAuthorIdentity) save.Children.Add(Action("Continue to Changed files", () => Navigate("Changes"), true));
        else save.Children.Add(Action("Set author details first", () => Navigate("Author setup"), true));
        if (recorded) PageContent.Children.Insert(nextActionIndex, Card(save));
        else AddCard(PageContent, save);
    }

    private void AddGuideSection(Panel content, string title, bool complete, bool? finished = null)
    {
        // Short walkthrough hides only finished steps; upcoming ones stay visible but collapsed.
        if ((finished ?? complete) && Welcome.ShowsShortWalkthrough(beginner.Experience)) return;
        var section = new Expander { Header = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap }, Content = content, IsExpanded = !complete };
        System.Windows.Automation.AutomationProperties.SetName(section, title);
        AddCard(PageContent, section);
    }

    private void BuildAuthorSetup()
    {
        Heading("Author details", "These details identify your saved versions. They are separate from GitHub sign-in.");
        if (model.Snapshot is null || model.Snapshot.Project.Kind is not (RepositoryKind.WorkingTree or RepositoryKind.LinkedWorktree))
        { AddCard(PageContent, "Set up local history in a project before saving author details."); PageContent.Children.Add(Action("Back to walkthrough", () => Navigate("Walkthrough"))); return; }
        var form = new StackPanel();
        Text(form, "Any name and email are fine, for example \"Asha Rao\" and asha@example.com. They are shown on each version you save, and on GitHub if you upload. Want to keep your email private on GitHub? Use the private address GitHub gives you (Settings > Emails on github.com).", true);
        var name = Input(form, "Your author name", model.Snapshot.AuthorName ?? "");
        var email = Input(form, "Your author email", model.Snapshot.AuthorEmail ?? "");
        ExplainBeginnerAccess(form);
        form.Children.Add(OperationButton("Preview saving your name and email", OperationKind.ConfigureIdentity, async () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(email.Text)) { Banner("Enter both your author name and email before reviewing."); return; }
            await RequestAsync(OperationKind.ConfigureIdentity, authorName: name.Text.Trim(), authorEmail: email.Text.Trim());
        }));
        AddCard(PageContent, form);
        var actions = Row(); actions.Children.Add(Action("Back to walkthrough", () => Navigate("Walkthrough"))); actions.Children.Add(Action("Back to Changed files", () => Navigate("Changes"))); PageContent.Children.Add(actions);
    }

    private async Task CreatePracticeProjectAsync()
    {
        if (workspace is null) { Banner(setupError ?? "Set up Git first to create a practice project."); Navigate("Settings"); return; }
        var picker = new OpenFolderDialog { Title = "Choose where to create a new GitCompass practice folder", Multiselect = false };
        if (picker.ShowDialog(this) == true) await CreatePracticeAtAsync(picker.FolderName);
    }

    private async Task CreatePracticeAtAsync(string parent)
    {
        if (workspace is null || model.Busy) return;
        string? folder = null;
        var created = false;
        await WorkAsync(async () =>
        {
            // Inspect before writing sample files, so practice cannot change a real repository.
            var parentSnapshot = await workspace.OpenAsync(parent);
            if (parentSnapshot.Project.Kind != RepositoryKind.Candidate)
            { Banner("Choose a location outside an existing Git project. Practice should have its own separate folder."); return; }
            folder = Path.Combine(Path.GetFullPath(parent), "GitCompass practice " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + " " + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "Learning-notes.txt"), "My first saved version\n\nI am learning to keep versions on my computer.\nSaving locally and sharing online are separate steps.\n");
            beginner = beginner with { Active = true, WelcomeDismissed = true, ProjectFolder = folder, Intent = "OwnFiles", PracticeFolder = folder, GuidedPractice = false, TeamCopyFolder = null, SavedCommitId = null };
            await SaveBeginnerAsync();
            created = true;
        });
        if (!created || folder is null) return;
        await OpenProjectAsync(folder);
        Navigate("Walkthrough");
    }

    /// <summary>Walkthrough-only, explicitly confirmed: creates a Git-ready practice repository. The legacy sample folder never initializes Git.</summary>
    private async Task CreateGuidedPracticeProjectAsync()
    {
        if (workspace is null) { Banner(setupError ?? "Set up Git first to create a practice project."); Navigate("Settings"); return; }
        var picker = new OpenFolderDialog { Title = "Choose where to create the guided practice project", Multiselect = false };
        if (picker.ShowDialog(this) == true) await CreateGuidedPracticeAtAsync(picker.FolderName, confirmed: false);
    }

    private async Task CreateGuidedPracticeAtAsync(string parent, bool confirmed)
    {
        if (workspace is null || model.Busy) return;
        var executable = settings.GitExecutablePath ?? DependencyProbe.FindGitExecutable();
        if (executable is null) { Banner(setupError ?? "Set up Git first to create a practice project."); Navigate("Settings"); return; }
        var target = Path.Combine(Path.GetFullPath(parent), PracticeProject.FolderName);
        // Review before any write: this is the only path that runs git init, and only in a new folder.
        if (!confirmed && MessageBox.Show(this,
            "GitCompass will create a new folder:\n" + target + "\n\nInside it, Git will set up version history (git init), set a practice author for this folder only, save one first version, then add one edit and one new file for you to practise with.\n\nNo existing folder is changed and nothing is uploaded.",
            "Review: create practice project", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        string? folder = null;
        await WorkAsync(async () =>
        {
            var parentSnapshot = await workspace.OpenAsync(parent);
            if (parentSnapshot.Project.Kind != RepositoryKind.Candidate)
            { Banner("Choose a location outside an existing Git project. Practice should have its own separate folder."); return; }
            try { folder = await PracticeProject.CreateAsync(executable, parent); }
            catch (IOException error) { Banner(error.Message); return; }
            beginner = beginner with { Active = true, WelcomeDismissed = true, ProjectFolder = folder, Intent = "OwnFiles", PracticeFolder = folder, GuidedPractice = true, TeamCopyFolder = null, SavedCommitId = null, MissionIndex = BeginnerMissions.SeeChange, PushOffered = false };
            await SaveBeginnerAsync();
        });
        if (folder is null) return;
        await OpenProjectAsync(folder);
        Navigate("Walkthrough");
    }

    private bool IsPracticeProject => beginner.GuidedPractice && model.Snapshot is not null && SameFolder(beginner.PracticeFolder, model.Snapshot.Project.SelectedFolder);

    /// <summary>The "small moment": a ✓ banner naming the mission just finished, what it means, and what's next.</summary>
    private void CelebrateMission(int completed, int next) =>
        Banner($"✓ Mission {completed + 1} of {BeginnerMissions.Count} complete. {BeginnerMissions.Celebration(completed)} " +
            (next >= BeginnerMissions.Done ? "That's all five. Open the walkthrough for your recap." : "Next: " + BeginnerMissions.Missions[next].Title + "."));

    /// <summary>Called when a file diff is opened in Changed files. Only the welcome.txt diff completes mission 1.</summary>
    private async Task OnBeginnerDiffViewedAsync(string path)
    {
        if (!beginner.Active || !IsPracticeProject) return;
        var next = BeginnerMissions.ViewedDiff(beginner.MissionIndex, path);
        if (next == beginner.MissionIndex) return;
        var completed = beginner.MissionIndex;
        beginner = beginner with { MissionIndex = next };
        await SaveBeginnerAsync();
        CelebrateMission(completed, next);
    }

    private async Task SetMissionAsync(int index, string? page = null)
    {
        beginner = beginner with { MissionIndex = BeginnerMissions.Clamp(index) };
        await SaveBeginnerAsync();
        if (page is null) RenderPage(); else Navigate(page);
    }

    private void BuildBranchSteps(Panel content)
    {
        if (beginner.PracticeBaseBranch is null && model.Snapshot?.Branch is { } current && current != BeginnerMissions.PracticeBranch)
        { beginner = beginner with { PracticeBaseBranch = current }; _ = SaveBeginnerAsync(); }
        var steps = BeginnerMissions.BranchSteps(PracticeBase);
        var step = Math.Clamp(beginner.BranchStep, 0, 3);
        Text(content, $"Your files follow the line of work you're on. Switching back to {PracticeBase} hides ideas.txt until you combine them. Nothing is lost.", true);
        for (var i = 0; i < steps.Length; i++)
        {
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var label = (i < step ? "✓ " : i == step ? "→ " : "○ ") + $"Step {i + 1} of 4: " + steps[i];
            var line = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontWeight = i == step ? FontWeights.SemiBold : FontWeights.Normal };
            line.SetResourceReference(TextBlock.ForegroundProperty, i == step ? "TextBrush" : "MutedBrush");
            AutomationProperties.SetName(line, (i < step ? "Done. " : i == step ? "Current step. " : "Not started. ") + $"Step {i + 1} of 4: " + steps[i]);
            row.Children.Add(line);
            if (i == step)
            {
                var (title, page) = i switch
                {
                    0 => ($"Open Branches to create {BeginnerMissions.PracticeBranch}", "Branches"),
                    1 => ("Open Changed files to save ideas.txt", "Changes"),
                    2 => ($"Open Branches to switch back to {PracticeBase}", "Branches"),
                    _ => ("Open Saved versions to combine (merge)", "History graph"),
                };
                row.Children.Add(Action(title, () => Navigate(page), true));
                if (i == 1 && model.Snapshot?.Branch != BeginnerMissions.PracticeBranch) Text(row, $"First switch to {BeginnerMissions.PracticeBranch} on the Branches page, then save.", true);
            }
            content.Children.Add(row);
        }
    }

    private void BuildMissions()
    {
        var index = BeginnerMissions.Clamp(beginner.MissionIndex);
        Heading("Practice missions", index >= BeginnerMissions.Done ? "All missions complete." : $"Mission {index + 1} of {BeginnerMissions.Count} · every change is reviewed before it runs.");
        // Pause/Exit come from BuildWalkthrough, which hosts this page; adding them here duplicated the buttons.
        var progress = new ProgressBar { Minimum = 0, Maximum = BeginnerMissions.Count, Value = index, Height = 8, Margin = new Thickness(0, 8, 0, 4) };
        AutomationProperties.SetName(progress, BeginnerMissions.ProgressLabel(index)); PageContent.Children.Add(progress);
        Text(PageContent, BeginnerMissions.ProgressLabel(index), true);
        if (index >= BeginnerMissions.Done)
        {
            var recap = new StackPanel(); Subheading(recap, "You did it");
            foreach (var mission in BeginnerMissions.Missions) Text(recap, "✓ " + mission.Title + " — " + mission.Concept, true);
            recap.Children.Add(Action("Open my own project", () => PickFolder(this, new RoutedEventArgs()), true));
            recap.Children.Add(Action("Finish walkthrough", PauseBeginnerAsync));
            Text(recap, "The practice folder stays at " + beginner.PracticeFolder + ". You can delete it yourself at any time.", true);
            AddCard(PageContent, recap);
            return;
        }
        var access = new StackPanel(); Subheading(access, "Project access"); ExplainBeginnerAccess(access);
        if (workspace is not null && model.Snapshot is not null)
        {
            var grant = workspace.Access.Get(model.Snapshot.ProjectId);
            AddGuideSection(access, "Project access · " + AccessText(grant.Level), grant.Level == AccessLevel.AllowChanges && grant.PendingLevel is null);
        }
        for (var i = 0; i < BeginnerMissions.Count; i++)
        {
            var mission = BeginnerMissions.Missions[i];
            var content = new StackPanel(); Subheading(content, $"{i + 1}. {mission.Title}");
            if (i == index)
            {
                Text(content, mission.Goal); Text(content, mission.Concept, true);
                var actions = Row();
                switch (i)
                {
                    case BeginnerMissions.SeeChange: Text(content, "Tip: in Changed files, select welcome.txt to see what changed.", true); actions.Children.Add(Action("Open Changed files", () => Navigate("Changes"), true)); break;
                    case BeginnerMissions.Stage: case BeginnerMissions.Commit: actions.Children.Add(Action("Open Changed files", () => Navigate("Changes"), true)); break;
                    case BeginnerMissions.BranchMerge:
                        BuildBranchSteps(content);
                        actions.Children.Add(Action("Learn about merging", () => ShowLesson("git-merge")));
                        break;
                    case BeginnerMissions.Push:
                        Text(content, "Optional. Your practice files will be visible to anyone who can see the repository you upload to.", true);
                        Text(content, "Uploading takes 3 short steps on the next page: a free GitHub account, an empty repository, and its web address.", true);
                        actions.Children.Add(Action("Upload to GitHub (3 steps)", BuildProjectSetup));
                        actions.Children.Add(Action("Finish without uploading", () => SetMissionAsync(BeginnerMissions.FinishWithoutPush(index)), true));
                        break;
                }
                content.Children.Add(actions);
            }
            else Text(content, i < index ? "Complete. " + mission.Concept : "Coming up.", true);
            AddGuideSection(content, $"{i + 1}. {mission.Title}" + (i < index ? " · done" : i == index ? " · now" : ""), i != index, finished: i < index);
        }
    }
}
