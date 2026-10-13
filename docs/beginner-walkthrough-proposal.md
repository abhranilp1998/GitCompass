# Proposed beginner walkthrough

The [implementation and verification record](beginner-fluent-update.md) describes the current source update and the remaining limits of the proposal below.

13 October 2026. This proposal follows the current-source [beginner experience review](F:/Dev/Projects/GitCompass/docs/beginner-experience-review-2026-10-13.md). The audience has no Git or GitHub knowledge. The user clarified that the whole application should be beginner friendly and feel like the **Windows 11 Settings app**, with modern Fluent styling, System/Light/Dark themes, and a walkthrough for newcomers. Five alternative designs were not requested; the five scenario routes below are coverage cases for one consistent experience. WPF is acceptable, and a framework change is optional rather than a requirement. Touch-specific optimization is not a confirmed requirement.

## Approach

Apply the shared Windows 11 design system across Home, project selection/setup, Changes, history, branches, sharing/pull requests, Learn, Settings, permissions, operation reviews, problems and activity/terminal surfaces. Reuse typography, icons, spacing, navigation, theme resources and control states. Beginner wording and contextual explanations should remain consistent outside the first-run walkthrough as well.

Use a Windows 11 Settings-style workspace with an adaptive walkthrough inside the relevant page. Keep a familiar sidebar, search, page titles, breadcrumbs, and grouped rows. The walkthrough guides the first successful task and then becomes contextual help. Its steps depend on whether the selected folder needs setup, already has useful local history, belongs to a team, or contains an interrupted operation.

First launch offers **Take a guided walkthrough**, **Choose my project folder**, and a secondary **Try a practice project** route. The walkthrough is optional, resumable and accessible again from Learn. It uses the selected project state or a disposable practice project, explains effects before action, and does not run Git operations simply because a tutorial step is advanced.

The common entry is **Choose project folder → check prerequisites and scope → explain the detected state → choose a relevant goal → guide one successful task**. Choosing a folder never initializes or downloads anything automatically. Each guided write still passes through the current permission and reviewed-operation engine.

Teach one distinction at a time:

- Saving a file in an editor changes the working file.
- Saving a version with Git records selected file contents on this computer.
- Uploading sends recorded versions to the reviewed online destination.
- Checking for team updates refreshes information; applying updates changes project files.

Use “Changed files,” not “Unsaved changes,” for Git-observed edits: those files may already be saved in the user's editor.

## Five routes

### 1. My original project folder has no Git

**Page:** Projects → This project → Set up version history.

**Opening screen:** “This folder does not remember earlier versions yet. You can start a history on this computer. Nothing will be uploaded.”

**Main action:** Start remembering versions.

**Walkthrough:** confirm project scope → explain/enable the required changes permission → review starting history in the selected folder → inspect suggested exclusions and selected files → collect and explain author name/email if missing → describe the version → review and save it locally.

The branch defaults to `main`, with its meaning available in advanced details. Name/email inputs can be collected earlier, but repository-local configuration is executed only after initialization succeeds. Excluded/private files are reviewed before the first commit. Existing staged contents remain explicit in the final version review.

**Completion:** “Your first version is saved on this computer. It has not been uploaded.” Show the version and the files it includes, then offer optional online sharing.

### 2. My team gave me a link, ZIP or Drive copy

**Page:** Projects → Get a team project.

For an ordinary folder, first ask whether these are the user's original files or a copy of a team project. Folder contents alone cannot establish that intent.

**Opening screen:** “Your team may already have the project's version history. Download that history into a separate folder and keep this copy.”

**Main action:** Get my team's project.

**Walkthrough:** enter the supplied project link → select a separate empty destination → explain/enable permission and check required sign-in → review download → inspect the downloaded project → if there is edited work in the old copy, compare and select changes to transfer → review and save the intended changes.

If the user has no project link, explain exactly what to obtain from the team. Do not recommend initializing a new unrelated history as the default substitute. Preserve the old copy; never copy its `.git` directory over the new project. A guided compare/transfer experience is new proposed capability and needs deliberate file-preservation design.

**Completion:** the established team history is available and the original copy remains intact. Any transferred edits have their own reviewed state.

### 3. This folder already uses Git, with no online connection

**Pages:** Home for state, Changed files for a local save, Sharing for optional online setup.

**Opening screen:** “This project already remembers versions on this computer. Online sharing is optional.”

**Main action:** Save another version, when changed files exist; otherwise view the latest saved version.

**Walkthrough:** show the latest recorded version and changed files → review selected contents → collect missing author identity → review/save a local version.

Replace inapplicable team-count question marks with “No online connection configured.” Do not repeatedly recommend fetching a remote that does not exist. If the user chooses online sharing, guide creation/selection of the appropriate hosted repository, privacy and destination choices, transport authentication, and a reviewed upload. An existing remote without tracking needs its own use-existing-connection path; the current add-only form cannot fulfill that case directly.

**Completion:** another local version is available without requiring a GitHub account.

### 4. This project is connected to my team

**Pages:** Home → Team updates, Changed files → Save version, Sharing → Upload saved versions.

**Opening screen:** show Changed files, versions recorded locally, the known upload/comparison status, and the last successful team check. Unknown status remains explicitly unknown until checked.

**Main action:** the next applicable step from current project facts.

**Walkthrough:** review changed files → preserve/save the intended work → check for team updates with permitted access → review incoming versions → apply a supported clean update or enter integration/recovery when histories diverge → review the exact destination and versions → upload → verify the result.

Do not offer one universal “Sync everything” action that conceals checking, file changes, and upload. The interface may sequence these decisions, while explaining each distinct effect. An offline check does not invalidate locally recorded versions, and a rejected upload does not imply that those versions disappeared.

**Completion:** the user can explain what is saved on this computer, what was verified as uploaded, and whether project files received team updates. Cached comparisons are still labeled accurately after an upload.

### 5. Something needs attention

**Page:** Home → Resolve this problem, with a breadcrumb to the originating action.

**Opening screen:** state the concrete problem, what is known to remain preserved, and the next useful action.

**Main action:** Fix this step, with a problem-specific label where possible.

**Walkthrough:** missing author identity → name/email setup; missing access → permission explanation; missing connection or credentials → connection setup; conflicted merge → affected file and version review, deliberate editing, reviewed resolution and completion; uncertain upload → inspect/verify the destination before repeating it.

Ordinary update/upload advice yields to the recorded interrupted-operation state. Complex unsupported recovery explains the limitation and offers reviewed diagnostics for human help. A file-by-file beginner conflict assistant is proposed new capability; current Explorer/editor recovery does not prove it exists.

**Completion:** the remedy is verified, or the user understands what remains preserved and the precise help still required.

## Shared gates

**Git missing:** retain the selected folder; show a focused explanation and official installation guidance, then recheck/resume. Executable-path selection belongs in advanced setup. Do not promise an installer is bundled in the current app.

**Nested folder:** prominently say “You selected a folder inside this project. Git tracks the whole project shown here.” Repeat effective scope before writes.

**Multiple projects or invalid metadata:** help select the intended project rather than defaulting to initialization of a collection folder. Keep discovery bounded; arbitrary recursive scanning should not be automatic.

**Permission:** preserve the existing Read-only / Monitor only / Allow changes enforcement. Put the required choice beside the relevant action. Technical hook/filter/transport details remain available; clear wording does not remove deliberate trust or review.

## Windows 11 UI direction

The concrete reference is Windows 11 Settings: navigation by named categories, searchable actions, and dedicated pages containing grouped controls and links to subpages. Adapt that structure to project work. [Microsoft's Settings overview](https://support.microsoft.com/en-US/Windows/Experience/exploring-windows-settings)

Retain the existing WPF application and build on its Fluent theme. WPF provides Windows 11 styling, System/Light/Dark modes and system accent resources; a framework rewrite is not required for this direction. [Microsoft WPF guidance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90)

Use the left column for project identity, a project selector, “Find an action or explanation,” and the main categories: Home, Projects, Changed files, Saved versions, Sharing, Learn, and Settings. Keep branch management and terminal in advanced pages. Search maps everyday phrases such as “save my work” to the relevant existing action/lesson; no online AI dependency is required for basic search.

The main area uses a breadcrumb, page title, short description, and grouped rows. Each row pairs an icon/title and explanation on the left with its status, button, selector or navigation chevron on the right. Use toggles for durable preferences such as notifications; initialization, saving, applying updates and uploading remain explicit reviewed actions. Do not represent an operation as a toggle that immediately changes repository state.

For example, the project page has rows for Project folder, Version history, Files for the next version, Author details, Online sharing, and Project permissions. Selecting Version history opens its setup subpage. The current prerequisite appears first, and irrelevant rows are deferred or explained. A team-copy decision is resolved before offering initialization.

Keep one dominant action on the current step; place Back and valid defer/skip choices consistently. Progress reflects actual completed steps. Provide expandable “What will happen?” and “Show Git details.” These appear inline in the Settings-style page; the user can resume without restarting completed work. The walkthrough is not a full-screen slideshow or a chain of blocking instructional dialogs.

Move activity details and terminal out of the permanently expanded first-use area. Keep visible operation progress and actionable problems. Use consistent shared controls, Segoe typography, Windows icon resources, restrained rounded corners and spacing. Native folder/file pickers retain familiar Windows behavior. Respect system themes and high contrast, including runtime changes; the existing hardcoded blue/custom palettes need review if system accent following is intended.

Mica is a restrained window-base material where supported; it is not a requirement to blur every panel. Use an opaque fallback and respect accessibility/device conditions. [Microsoft Mica guidance](https://learn.microsoft.com/en-us/windows/apps/design/style/mica)

Use explicit file checkboxes, comfortable spacing, visible keyboard focus and explanations that do not depend on hover. Mouse and keyboard are the baseline. Touch-specific density and device acceptance can be added if requested; they are not part of the confirmed brief.

Use inline field errors with direct remedies. Reserve blocking confirmation for meaningful decisions and reviewed effects; ordinary instructional pages do not each require a modal. [Microsoft dialog guidance](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs)

## Concrete first-save walkthrough

1. Choose an ordinary folder containing project files.
2. See “This folder has no version history yet,” its actual path, and the question about original files versus a team copy.
3. Choose Start remembering versions. Explain that this starts local history and does not publish the project.
4. Enable the relevant project permission deliberately and review initialization.
5. Review files/exclusions and author identity. Show missing prerequisites before an attempted save.
6. Select a file with a checkbox and inspect its contents. Describe the version in ordinary language.
7. Review the exact selected and pre-existing staged contents, project and effects, then save.
8. See the recorded version and “Saved on this computer; not uploaded.” Choose to finish, practice another edit, or set up online sharing.

## Implementation sequence and acceptance

First deliver shared detection/prerequisite/permission states and route 1 through a successful local save. Then support route 3 and direct error remedies; add the team-copy and connected-project journeys with hosted setup. Complex recovery is a separate larger capability.

Reuse components for identity, permission, file review, connection and result verification across routes. Keep routing decisions testable outside `ShellWindow.xaml.cs` and central operation enforcement intact. A wizard visual alone does not close missing backend capabilities.

Review the proposed visual treatment before app implementation. Then capture the built states at small laptop sizes, snapped windows, light/dark/high contrast and 100/150/200% scaling; verify mouse, keyboard and touch behavior where devices are available. Finally observe zero-knowledge participants completing the five routes without being given their next click. Proposed designs and rendered previews are not beginner acceptance evidence.
