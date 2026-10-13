# Git Project Assistant — visual concepts

Prepared 12 October 2026. Working product name; fictional projects and sample data. These are AI-generated design concepts, not screenshots of implemented software. Four screens explore one application in light and dark themes.

## Chosen stack

**C# + .NET 10 LTS + WPF with Fluent styling.** This is the recommended starting point for the Windows desktop application. WPF provides desktop controls, data binding and vector graphics; its current Fluent theme provides a modern Windows appearance. The recommendation favors the user's resizable project workspace, graph, output panels and terminal integration. It is an architectural judgment, not a measured performance comparison. Microsoft's documentation confirms both the [WPF desktop capabilities](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/) and [continuing Fluent improvements in .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100). [.NET 10 is an LTS release](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

Use the installed Git command-line engine for repository operations, a separate GitHub API adapter for GitHub features, an MVVM presentation layer, and a deterministic repository-state rules engine for recommendations. For the real integrated terminal, prototype ConPTY with a local xterm.js frontend in WebView2. Microsoft documents [pseudoconsole session hosting](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session) and [WebView2 integration in WPF](https://learn.microsoft.com/en-us/microsoft-edge/webview2/get-started/wpf). Terminal hosting is a required technical spike before the stack is committed to implementation.

[WinUI 3](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/) remains a credible alternative; Microsoft recommends it for new modern Windows applications. WPF is selected here because of the desktop workspace requirements and its established .NET desktop ecosystem. Its Fluent styling still requires a prototype and visual testing; support is continuing to improve. [Electron](https://www.electronjs.org/docs/latest/tutorial/process-model) is an alternative if the team later prioritizes web UI experience or cross-platform delivery. No alternative is claimed to be inherently faster or easier without a prototype.

Verify the colleagues' actual Windows builds and editions before promising OS compatibility. Modern Windows styling and supported operating systems are separate decisions; the [.NET 10 Windows support matrix](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) must be checked against the deployment machines.

## Screen set

| Screen | Purpose | What to review |
| --- | --- | --- |
| [1. Select a project folder](01-project-folder.png) | Primary entry point, familiar Windows folder picker, recent projects and a preview of the workflow. | Is the folder action obvious? Does the inspection message make opening a folder comfortable? |
| [2. Status and next steps](02-status-and-next-steps.png) | Separate file changes, incoming commits and outgoing commits; explain an ordered plan and the difference between fetch and pull. | Can a beginner identify the next action and why it matters? |
| [3. History and learning](03-history-and-learning.png) | Show a common ancestor, local work and team work with contextual explanations. | Is divergence understandable without prior knowledge of Git graphs? |
| [4. Output and terminal](04-output-and-terminal.png) | Separate operation logs, concurrent background processes, terminal tabs, error explanations and a support view. | Can the user identify what failed and prepare a useful screenshot or copied report? |

### 1. Select a project folder

The app inspects a selected folder and reports what it finds. Opening the folder does not initialize Git, create commits or integrate changes. A first-time project without Git will need its own onboarding plan in the functional specification. Preview information is explicitly labeled as sample content.

### 2. Status and next steps

Example: four changed files, two incoming commits and no outgoing commits. The user reviews and saves work before choosing how to integrate team changes. A local commit can create divergence, so the app must re-analyze the repository after each action and adjust the plan. Zero outgoing commits does not imply the branch is up to date. Fetch updates knowledge of the remote; pull includes integration. This follows the official [fetch](https://git-scm.com/docs/git-fetch) and [pull](https://git-scm.com/docs/git-pull) behavior.

### 3. History and learning

Example: one local commit and two remote commits share an earlier ancestor. The graph labels the local and remote branches separately and explains why an integration choice is needed. The application should introduce branches and pull requests gradually while retaining access to these features.

### 4. Output and terminal

The output pane stores app-managed operation logs. The terminal hosts a real interactive shell. Each repository, process and terminal session is identifiable. Different repositories may have concurrent activity; operations that mutate the same repository require coordination. After terminal commands, the app refreshes repository state. A terminal's local status can reflect cached remote-tracking information until a remote check succeeds; the UI must make that freshness clear. An upload rejection prompts a new remote check and review, not an automatic retry or history rewrite.

The support view should include project, branch, operation, result, app/Git versions and relevant redacted output, with a review step before sharing. A screenshot helps with initial triage; difficult failures may still require copied logs. Sending a report is a user action.

## Decisions to carry into the documentation

- Make folder selection available throughout the application.
- Keep one prominent recommended next action, with reasons and an optional explanation.
- Keep Git Output, Problems and Terminal accessible in a resizable dock; preserve separate output channels and terminal tabs.
- Explain freshness of remote information and distinguish local inspection from remote checks.
- Refresh the plan after each operation; never derive advice from stale counts.
- Cover ZIP/Drive migration, `.gitignore`, first publishing, daily synchronization, conflicts, branches and pull requests in the functional specification and SOP.
- Choose a consistent navigation and icon set during implementation. The generated screens explore minor layout variations.

## Accepted application direction

The user approved combining these views into one application. Keep one project selector, consistent navigation and a shared output/terminal dock. Add a visible Access mode control: new projects open in Read-only; Monitor only enables narrowly scoped fetch and hosted reads; Allow changes explicitly unlocks guided operations and the terminal for the current session. Enforce access in the operation engine, scheduler and terminal bridge.

The complete implementation brief and operating procedures are in [Git_Project_Assistant_Build_Spec.md](Git_Project_Assistant_Build_Spec.md). Use [CODEX_START_HERE.md](CODEX_START_HERE.md) for the short implementation handoff.

## Generation record

All concept images were generated with the built-in Imagegen tool. The light status concept received one targeted correction to its branch-status label, graph and integration wording. Original generation prompts and the correction prompt are recorded in [Concept_Prompts.md](Concept_Prompts.md).
