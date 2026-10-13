# GitCompass beginner experience review

Reviewed 13 October 2026. Audience: someone who has never used Git, GitHub, branches, commits, or a terminal. Their first task is to choose a project folder, understand whether it already uses Git, and save a useful first version without needing a colleague to explain the next click.

## Verdict

GitCompass has useful safeguards and several clear explanations, but the current experience does not yet support independent use by an absolute beginner. It is closer to a careful assistant for someone learning Git with help. The largest gap is the sequence of decisions: the interface presents Git setup details before establishing the user's intent and collecting the prerequisites.

Opening ordinary folders and existing Git projects works in the current rendered application. Supporting both folder types is necessary, but the beginner also needs a clear route from that detection to a successful first save. Online collaboration introduces additional setup that the current application does not guide completely.

## Current evidence and limits

The current source compiled successfully with zero warnings and zero errors. Eight native WPF states were rendered using the application's existing smoke capture support, with isolated application state and disposable folders. Each saved PNG was opened and inspected during this review. All project captures used Read-only. The ordinary folder retained its contents and had no `.git` directory afterward; file-content manifests for both fixtures matched before and after inspection. No remote was configured, no fetch or upload occurred, and the capture processes exited.

These captures show the actual WPF content, produced by `RenderTargetBitmap`, rather than a mockup or an operating-system screenshot. They do not exercise the native folder picker, human trust confirmation, keyboard interaction, screen reader, first-save completion, live GitHub sign-in, or upload. The author-identity and online-setup findings below come from current source tracing. No beginner participant was observed, so independent task completion remains unverified.

The workspace itself has no Git repository; a baseline `git status --short` reported that fact. Application source was not changed. The captures and their repeatable script are under [.artifacts/beginner-audit-20261013](F:/Dev/Projects/GitCompass/.artifacts/beginner-audit-20261013/capture.ps1).

## Reviewed steps

### 1. First launch — clear entry, too much surrounding detail

![First launch](F:/Dev/Projects/GitCompass/.artifacts/beginner-audit-20261013/01-first-run.png)

The prominent **Select project folder** button is easy to find. The explanation that inspection does not change files is reassuring. However, Branch, Access, History graph, Branches, Pull requests, Git Output, and Terminal are all visible before the user knows what Git does. The welcome instructions also refer to an empty destination for a clone without explaining that concept.

Keep the folder action. Add a short explanation: “Git can remember versions of your project on this computer. GitHub can store and share them online.” This distinction follows [GitHub's explanation of Git and GitHub](https://docs.github.com/en/get-started/start-your-journey/what-is-github). Let the user choose their folder without first deciding whether Git is present.

### 2. Ordinary folder, Overview — detection works; setup is difficult

![Ordinary folder overview](F:/Dev/Projects/GitCompass/.artifacts/beginner-audit-20261013/02-ordinary-overview.png)

The header correctly says **Not using Git**. Opening the folder has not initialized it. The main page immediately presents **Initial branch**, **Review initialization**, **Clone an existing repository**, and **Repository URL**, with disabled action buttons in Read-only. The text introduces canonical repositories, ignore rules, publication, and `.git`.

This asks the beginner to diagnose the correct Git operation. A ZIP copy from a teammate needs a different route from an original project the user owns. The page should first offer “Start remembering versions of my files,” “Download the project my team shared,” and “Just look around.” Clone should request a separate empty destination when the selected folder already contains files; the existing backend correctly refuses to overwrite a populated folder.

### 3. Ordinary folder, Changes — useful reassurance and direction

![Ordinary folder changes](F:/Dev/Projects/GitCompass/.artifacts/beginner-audit-20261013/03-ordinary-changes.png)

**This folder is not using Git yet**, **Your existing files are preserved**, and the explanation that GitHub sign-in is optional are useful. The page offers a clear main setup action rather than pretending that an ordinary folder has Git history. This is a better model for the first detected-folder result, though staged/unstaged terminology can still be deferred.

### 4. Existing Git project, Overview — useful facts, unfamiliar interpretation

![Git project overview](F:/Dev/Projects/GitCompass/.artifacts/beginner-audit-20261013/04-git-overview.png)

The changed-file count and **Review changed files** action provide a useful starting point. The local fixture has no remote, but the page shows question marks for team-only and local-only commits, **No tracking branch configured**, and cached-reference language. The next-step engine also recommends a connection and remote check for a local-only repository.

Preserve honest unknown values. Explain the cause in ordinary language: “This project is saved on this computer and has no online connection.” Offer online connection as a choice instead of treating local-only history as unfinished setup.

### 5. Existing Git project, Changes — good file descriptions; first save is fragmented

![Git project changes](F:/Dev/Projects/GitCompass/.artifacts/beginner-audit-20261013/05-git-changes.png)

**Modified**, **New file**, and **Not selected for commit** make file status more understandable. However, the first screen offers ignore suggestions and stash alongside stage/unstage controls. The commit-message section is below the initial viewport, while the output dock occupies a substantial part of the window. Multi-selection uses a standard extended-selection list, without a beginner selection affordance such as checkboxes.

Make the first-save sequence visible: choose files → review the version → describe it → save locally. Keep selection and review explicit, including any files already staged outside this journey. Offer an inline permission step when changes are disabled, retaining the existing access enforcement and separate review before execution.

### 6. Learn — useful reference, unsuitable starting lesson

![Learning page](F:/Dev/Projects/GitCompass/.artifacts/beginner-audit-20261013/06-learn.png)

Offline lessons are valuable, and the command finder is sensibly collapsed. The initial lesson nevertheless introduces staged contents and the index, displays technical command flags and placeholders, and asks the learner to use a local bare remote. The practice-folder button creates an ordinary folder; the learner must still work out initialization and subsequent steps.

The current catalog has 138 entries: five workflow lessons, 15 daily commands, and 118 advanced/plumbing references. A short guided practice journey should lead the beginner through one real file and one successful local version before showing upload commands. Keep the full reference available separately.

### 7. Settings — organized preferences; setup needs a dedicated route

![Settings page](F:/Dev/Projects/GitCompass/.artifacts/beginner-audit-20261013/07-settings.png)

Appearance, notification preferences, and advanced connection settings are separated. But when Git is absent, even selecting an ordinary folder routes here and requires installation or executable-path selection plus restart. A focused prerequisite screen should explain the missing tool, provide installation guidance, recheck it, and retain the user's selected folder.

GitHub API features additionally require a registered application client ID. That is administrator/deployment setup, not a reasonable task for this audience. Fetch/push credentials are a separate connection from API authorization, and the application should explain and check both where relevant.

### 8. Selecting a nested folder — parent project detected; scope needs explanation

![Nested project folder](F:/Dev/Projects/GitCompass/.artifacts/beginner-audit-20261013/08-nested-overview.png)

The application correctly finds the parent Git project and displays both the parent root and selected nested path. Its changed-file count includes files outside the selected subfolder. A beginner needs a prominent explanation: “You chose a folder inside this project. Git tracks the whole project shown here.” Show that scope again before an operation.

## Highest-impact changes

| Priority | Problem | Recommended change | Current source evidence |
| --- | --- | --- | --- |
| P1 | Non-Git setup demands initialization-versus-clone knowledge. | Ask whether these are the user's original files or a team project; guide the matching route. | [Folder setup](F:/Dev/Projects/GitCompass/src/GitCompass.Desktop/ShellWindow.xaml.cs:385), [candidate recommendation](F:/Dev/Projects/GitCompass/src/GitCompass.Domain/RecommendationEngine.cs:21) |
| P1 | First local save can fail because author name/email were never collected. | Check identity before the first save; explain it and offer direct setup. | [commit prerequisite](F:/Dev/Projects/GitCompass/src/GitCompass.Infrastructure.Git/GitBackend.Operations.cs:52), [first-version recommendation](F:/Dev/Projects/GitCompass/src/GitCompass.Domain/RecommendationEngine.cs:33), [generic problems](F:/Dev/Projects/GitCompass/src/GitCompass.Desktop/ShellWindow.xaml.cs:960) |
| P1 | First upload assumes an existing hosted repository URL and transport credentials. | Guide repository creation/selection, sign-in, destination and privacy choices; check the connection before upload. | [project setup](F:/Dev/Projects/GitCompass/src/GitCompass.Desktop/ShellWindow.xaml.cs:414), [API setup](F:/Dev/Projects/GitCompass/src/GitCompass.Desktop/ShellWindow.xaml.cs:835), [transport failure](F:/Dev/Projects/GitCompass/src/GitCompass.Infrastructure.Git/GitBackend.Operations.cs:399) |
| P1 | Learning begins after the basics; practice is a set of instructions rather than a guided success. | Explain folder/history/local/online first, then guide a disposable first save with checkpoints. | [default lesson](F:/Dev/Projects/GitCompass/assets/learning/manifest.json:140), [Learn implementation](F:/Dev/Projects/GitCompass/src/GitCompass.Desktop/ShellWindow.xaml.cs:779) |
| P1 | A fresh machine without Git cannot progress from folder selection. | Focused tool setup and recheck/resume journey. | [initialization](F:/Dev/Projects/GitCompass/src/GitCompass.Desktop/ShellWindow.xaml.cs:75), [opening](F:/Dev/Projects/GitCompass/src/GitCompass.Desktop/ShellWindow.xaml.cs:101) |
| P2 | Local-only projects receive connection/fetch recommendations. | Add explicit local-only intent and suppress inapplicable remote steps. | [recommendation conditions](F:/Dev/Projects/GitCompass/src/GitCompass.Domain/RecommendationEngine.cs:37) |
| P2 | An existing remote without tracking is directed toward a form that only adds a new remote. | Recognize existing connections and offer reviewed tracking setup separately. | [setup defaults](F:/Dev/Projects/GitCompass/src/GitCompass.Desktop/ShellWindow.xaml.cs:420), [existing-name rejection](F:/Dev/Projects/GitCompass/src/GitCompass.Infrastructure.Git/GitBackend.Operations.cs:117) |
| P2 | Disabled actions require a separate Access choice; trust copy assumes technical knowledge. | Put a clear permission step beside the action, with plain language and expandable technical detail. | [access confirmation](F:/Dev/Projects/GitCompass/src/GitCompass.Desktop/ShellWindow.xaml.cs:136), [disabled action](F:/Dev/Projects/GitCompass/src/GitCompass.Desktop/ShellWindow.xaml.cs:1037) |
| P2 | Logs and advanced navigation crowd every beginner state. | Collapse the activity dock initially; retain visible progress/problems and make details easy to open. | [shared shell](F:/Dev/Projects/GitCompass/src/GitCompass.Desktop/ShellWindow.xaml:15) |
| P2 | Folder selection can resolve to a larger parent project. | Explain detected scope explicitly before reviewed actions. | [discovery](F:/Dev/Projects/GitCompass/src/GitCompass.Infrastructure.Git/GitBackend.cs:33), [header](F:/Dev/Projects/GitCompass/src/GitCompass.Desktop/ShellWindow.xaml.cs:171) |

The identity requirement is not a GitHub-login requirement: Git labels commits with an author identity, and [GitHub documents that distinction](https://docs.github.com/en/get-started/git-basics/setting-your-username-in-git). Similarly, [GitHub's setup guidance](https://docs.github.com/en/get-started/git-basics/set-up-git) separates author setup from authenticating a connection. The app already acknowledges these distinctions; its journey needs to collect the right prerequisites at the right time.

## Proposed beginner journey

1. **Welcome:** “Choose your project folder. We'll check whether it already uses Git.” Offer a guided practice project as a second route.
2. **Detect and explain:** ordinary folder, existing Git project, or folder inside a larger project. Preserve files; explain detected scope.
3. **Ask intent:** “Remember versions on this computer,” “Work with my team's project,” or “Just look around.” For team work, obtain the supplied project link and a separate empty download destination when needed.
4. **Complete prerequisites:** installation if missing, author identity before the first local version, and an explicit permission step before changes. Keep branch defaults understandable and adjustable in advanced details.
5. **Save the first version:** choose files, review contents including existing staged work, enter a description, review effects, then save. Show “Version saved on this computer. It has not been uploaded.”
6. **Offer online collaboration:** explain GitHub, select/create the appropriate repository, review privacy and authorization, check transport access, and review the upload destination. Show verified publication only when it succeeds.
7. **Daily actions:** “Save a version,” “Check for team updates,” “Apply reviewed updates,” and “Upload saved versions.” Explain Git vocabulary beside these actions and expose command details when requested.
8. **Recover directly:** missing identity links to identity setup; missing connection links to connection setup; uncertain or complex conflicts explain what remains preserved and when help is needed.

These changes follow the principles of familiar language, visible next actions, and error prevention in [Nielsen Norman Group's usability heuristics](https://www.nngroup.com/articles/ten-usability-heuristics/). The proposed steps are recommendations derived from this application's current behavior, not evidence that the redesigned journey has been tested.

## Copy, components and accessibility

Use a shared action vocabulary across navigation, recommendations, lessons and previews: **Choose files for this version (Stage)**, **Save a version on this computer (Commit)**, **Check for team updates (Fetch)**, **Upload saved versions (Push)**, and **Ask the team to review my work (Pull request)**. Keep the distinction between a check and an update explicit.

The existing shared card, input and operation-button helpers are useful. Build reusable journey steps for permission, identity, connection and result states so an error can return to the same setup component. `ShellWindow.xaml.cs` currently combines these flows in a large class; concentrating beginner wording and prerequisite checks in shared components would reduce drift between Learn, Overview and Problems. Preserve the central operation permission and preview checks.

The captures show generous spacing and labeled controls, but also small muted helper text, a dense persistent dock, and important controls below the initial viewport. Verify keyboard selection, focus order, disabled-action explanations, announcements of status changes, high contrast, and text/DPI scaling on small laptops. The source supplies some automation names and high-contrast support; screenshots cannot establish accessibility acceptance.

## What would establish beginner readiness

Ask actual zero-knowledge users to complete the journey without being told the next click: open an ordinary folder, explain the detected state, start local history, save one selected file, reopen the app and find that version, then distinguish it from an uploaded version. Repeat with an existing team project and a nested folder. Include a fresh machine without Git and a missing-identity case. Observe whether each user can distinguish checking for updates from applying them.

Record independent completion, help required, wrong actions, and misunderstood wording. The existing [pilot checklist](F:/Dev/Projects/GitCompass/docs/pilot-checklist.md:5) is a useful foundation. Existing native workflow tests start with a preinitialized repository, configured identity, and injected fixture trust; they verify integration, not independent beginner onboarding.

The first implementation priority should be the complete **Select folder → detect Git → save the first local version** journey, followed by optional online collaboration.
