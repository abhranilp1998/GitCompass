using System.IO;
using System.Windows;
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
            var next = BeginnerMissions.Advance(beginner.MissionIndex, kind, state);
            if (next != beginner.MissionIndex)
            {
                beginner = beginner with { MissionIndex = next, SavedCommitId = kind == OperationKind.Commit ? model.Snapshot.HeadId : beginner.SavedCommitId };
                await SaveBeginnerAsync();
                Banner(next == BeginnerMissions.Done ? "All missions complete. Open the walkthrough for your recap." : "Mission complete. Next: " + BeginnerMissions.Missions[next].Title + ". Return to the walkthrough when you are ready.");
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
        guide.Children.Add(Action("Practice with a sample project", CreatePracticeProjectAsync));
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
            start.Children.Add(OperationButton("Review initialization", OperationKind.Initialize, async () => await RequestAsync(OperationKind.Initialize, branch: "main")));
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
            team.Children.Add(OperationButton("Review download of team project", OperationKind.Clone, async () =>
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

    private void ExplainBeginnerAccess(Panel target)
    {
        if (model.Snapshot is null || workspace is null) return;
        var grant = workspace.Access.Get(model.Snapshot.ProjectId);
        if (grant.Level != AccessLevel.AllowChanges || grant.PendingLevel is not null)
        {
            Text(target, "Before changing this project, use Project access above and choose Allow changes. Read the trust explanation first. This permission lasts for the app session; you can return to Read-only at any time.", true);
            target.Children.Add(Action("Show project access control", () => { AccessSelector.Focus(); AccessSelector.IsDropDownOpen = true; }));
        }
        else Text(target, "Allow changes is enabled. Each operation still needs its own review before it runs.", true);
    }

    private void BuildWalkthrough()
    {
        Heading("Your first saved version", "A guided local save, using your project's actual status. You can pause and return later.");
        var top = Row(); top.Children.Add(Action("Pause walkthrough", PauseBeginnerAsync)); top.Children.Add(Action("Exit walkthrough", ExitBeginnerAsync)); top.Children.Add(Action("Choose another project", () => PickFolder(this, new RoutedEventArgs()))); PageContent.Children.Add(top);
        if (model.Snapshot is null || workspace is null)
        {
            var choose = new StackPanel(); Subheading(choose, "1. Choose a project");
            Text(choose, "Select any folder with your project files, whether it uses Git or not. Or practice with an example in a new folder.", true);
            choose.Children.Add(Action("Select project folder", () => PickFolder(this, new RoutedEventArgs()), true));
            choose.Children.Add(Action("Create guided practice project", CreateGuidedPracticeProjectAsync));
            choose.Children.Add(Action("Practice with a sample project", CreatePracticeProjectAsync));
            AddCard(PageContent, choose);
            if (setupError is not null) { AddCard(PageContent, setupError); PageContent.Children.Add(Action("Open setup settings", () => Navigate("Settings"))); }
            return;
        }
        if (IsPracticeProject) { BuildMissions(); return; }
        var snapshot = model.Snapshot;
        var nextActionIndex = PageContent.Children.Count;
        // This is a historical record of a successful reviewed save, independent of
        // whichever branch comparison or page of history happens to be displayed.
        var recorded = IsGuideProject && beginner.SavedCommitId is not null;
        var selected = new StackPanel(); Subheading(selected, "1. Project selected"); Text(selected, snapshot.Project.DisplayName + "\n" + snapshot.Project.RootPath, true);
        if (!SameFolder(snapshot.Project.SelectedFolder, snapshot.Project.RootPath))
            Text(selected, "The folder you selected belongs to a larger Git project. Actions apply to the whole project shown above, including files outside the selected subfolder.", true);
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
        Text(files, "Staging means selecting contents for the next version. Saving includes all staged contents, including anything selected earlier. Review ignore suggestions for generated files and private information.", true);
        var stagedCount = snapshot.Files.Count(file => file.IsStaged);
        Text(files, $"Currently selected: {stagedCount} paths. Other edits stay in your working folder.", true);
        files.Children.Add(Action("Open Changed files", () => Navigate("Changes"), snapshot.HasAuthorIdentity));
        files.Children.Add(Action("Refresh local status", async () => await RefreshAsync()));
        AddGuideSection(files, recorded ? "5. Reviewed file contents were saved" : "5. Choose contents for your version", recorded);

        var save = new StackPanel();
        Subheading(save, recorded ? "6. Your version is saved on this computer" : "6. Save a version on this computer");
        Text(save, recorded ? (snapshot.HeadId == beginner.SavedCommitId ? "Saved version: " : "Earlier walkthrough save: ") + Short(beginner.SavedCommitId!) + ". " +
            (snapshot.HeadId == beginner.SavedCommitId ? "" : "The current branch now shows a different version. Inspect Saved versions to see its available history. ") +
            "Saving a version does not upload it. Online sharing is a separate reviewed action." : "On Changed files, describe what changed and choose Review local commit. A commit is a saved version. Check the project and all selected contents, then run the reviewed operation.", true);
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

    private void AddGuideSection(Panel content, string title, bool complete)
    {
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
        Text(form, "Use your own name and email, or the details agreed with your team. If you use a private GitHub email, copy the exact address from your GitHub email settings. These values will be stored only for this project.", true);
        var name = Input(form, "Your author name", model.Snapshot.AuthorName ?? "");
        var email = Input(form, "Your author email", model.Snapshot.AuthorEmail ?? "");
        ExplainBeginnerAccess(form);
        form.Children.Add(OperationButton("Review author identity change", OperationKind.ConfigureIdentity, async () =>
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

    private async Task SetMissionAsync(int index, string? page = null)
    {
        beginner = beginner with { MissionIndex = BeginnerMissions.Clamp(index) };
        await SaveBeginnerAsync();
        if (page is null) RenderPage(); else Navigate(page);
    }

    private void BuildMissions()
    {
        var index = BeginnerMissions.Clamp(beginner.MissionIndex);
        Heading("Practice missions", index >= BeginnerMissions.Done ? "All missions complete." : $"Mission {index + 1} of {BeginnerMissions.Count} · every change is reviewed before it runs.");
        var top = Row(); top.Children.Add(Action("Pause walkthrough", PauseBeginnerAsync)); top.Children.Add(Action("Exit walkthrough", ExitBeginnerAsync)); PageContent.Children.Add(top);
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
                    case BeginnerMissions.SeeChange: actions.Children.Add(Action("Open Changed files", () => SetMissionAsync(BeginnerMissions.ViewedChanges(index), "Changes"), true)); break;
                    case BeginnerMissions.Stage: case BeginnerMissions.Commit: actions.Children.Add(Action("Open Changed files", () => Navigate("Changes"), true)); break;
                    case BeginnerMissions.BranchMerge:
                        Text(content, "Files follow the branch you are on. Switching back to main hides ideas.txt until you merge; nothing is lost.", true);
                        actions.Children.Add(Action("Open Branches", () => Navigate("Branches"), true));
                        actions.Children.Add(Action("Open history graph", () => Navigate("History graph")));
                        actions.Children.Add(Action("Learn about merging", () => ShowLesson("git-merge")));
                        break;
                    case BeginnerMissions.Push:
                        Text(content, "Optional. Use an empty repository you own. Your practice files become visible to anyone who can see that repository.", true);
                        actions.Children.Add(Action("Set up an online connection", BuildProjectSetup));
                        actions.Children.Add(Action("Finish without uploading", () => SetMissionAsync(BeginnerMissions.FinishWithoutPush(index)), true));
                        break;
                }
                content.Children.Add(actions);
            }
            else Text(content, i < index ? "Complete. " + mission.Concept : "Coming up.", true);
            AddGuideSection(content, $"{i + 1}. {mission.Title}" + (i < index ? " · done" : i == index ? " · now" : ""), i != index);
        }
    }
}
