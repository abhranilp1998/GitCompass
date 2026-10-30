# GitCompass UI and UX review

Reviewed 12 October 2026 for people who may have no prior Git knowledge. The application is a native Windows WPF application. The four supplied concepts are visual direction; they are not evidence of completed functionality.

## Implementation and verification update

The initial findings below have been implemented in the native application. The current header explains Read-only, Monitor only and Allow changes without requiring a hover. File rows show Modified/New/Deleted and whether their contents are selected for the next commit; exact Git codes remain in details. History labels distinguish the local branch from the last fetched copy. Output has a visible search label and state selector, expandable operation groups, per-operation copying and a selectable combined-text view. GitHub API activity has its own channel with human-readable operation labels and final states. A mismatched published source is marked Stale and blocks submission; authorization failures are Denied. Output does not include HTTP bodies, authentication codes or tokens.

The daily learning journey is shown first. Lesson search and installed command coverage are expandable. Appearance and opted-in team checks precede a collapsed Advanced connection setup section. Notification settings include mute and local quiet hours; a monitoring notice can open the affected project without integrating files. Pull requests include read controls for status, reviews and checks, including a number lookup for closed/merged requests. Hosted account behavior still needs a configured app and live acceptance.

History now compares two selected branches using exact IDs from the current Git branch inventory. Relation counts come from Git. Paging retains the selected graph anchor. Text filtering affects only the loaded page and explicitly describes omitted parent endpoints. Successful upload evidence is retained for the session with the exact version, reviewed URL, branch and time; cached upstream counts are labeled local-only/team-only rather than being presented as upload results.

Changing projects clears the previous project's transient failure banner. Retained Problems entries open their originating project before preparing output or support information. Both editable support artifacts are shown before saving. Conflict recovery reveals the selected file in Windows Explorer using a fixed Explorer executable and typed arguments; it does not shell-open arbitrary project files or execute a file because of its extension. Tests inspect this safe command construction without launching Explorer on the user's desktop.

The reviewed dark primary pair is now dark text `#17243C` on `#3C93F2`, **4.91:1**. Light muted text `#5D6C82` gives **5.02:1** on the window, **5.34:1** on white cards and **4.74:1** on the pale tint. Hover keeps those colors; a visible outline indicates hover/keyboard focus. System high-contrast brushes override both primary text and graph colors. These are source-palette measurements against the [W3C normal-text contrast target](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html), not a whole-application accessibility certificate.

Release compilation completed with **0 warnings and 0 errors**. The page suite passed **14 real WPF light/dark render scenarios**. The final native interaction harness passed **23 checks** using its own disposable project, including selected-branch relation counts, graph filtering, operation groups/state filters, GitHub activity/result states without network requests, safe executable-looking file reveal construction, real selected file contents, canceling a staging preview, an actionable rejected empty commit, persistence of mute/overnight quiet hours/interval, first-run, non-Git onboarding and originating-project support review. The test compares the entire worktree, index, refs, objects and repository configuration before and after; they remained byte-for-byte unchanged. A new candidate's `.git` directory was not created. The exact successful outputs are persisted in [ui-final.log](../.artifacts/ui-final.log). The final two hosted-result semantics were verified by rerunning the 23 interaction checks after the helper change; the unchanged page suite was not repeated for that change.

Current page captures are under [the fourteen-page evidence folder](../.artifacts/desktop-tests/be5be75043804b82b91f5663d6442481). All seven light and seven dark pages were opened and visually inspected. Current interaction captures are under [the final native-state evidence folder](../.artifacts/desktop-tests/3c3bf81b4a7b49338314e362385bc560/native-states). They show [first run](../.artifacts/desktop-tests/3c3bf81b4a7b49338314e362385bc560/native-states/first-run-light.png), [selected branches](../.artifacts/desktop-tests/3c3bf81b4a7b49338314e362385bc560/native-states/selected-branches-light.png), [filtered history](../.artifacts/desktop-tests/3c3bf81b4a7b49338314e362385bc560/native-states/filtered-history-light.png), [selected diff](../.artifacts/desktop-tests/3c3bf81b4a7b49338314e362385bc560/native-states/selected-diff-light.png), [staging preview](../.artifacts/desktop-tests/3c3bf81b4a7b49338314e362385bc560/native-states/stage-preview-light.png), [actionable failure](../.artifacts/desktop-tests/3c3bf81b4a7b49338314e362385bc560/native-states/actionable-failure-dark.png), [notification settings](../.artifacts/desktop-tests/3c3bf81b4a7b49338314e362385bc560/native-states/notification-settings-dark.png), [non-Git onboarding](../.artifacts/desktop-tests/3c3bf81b4a7b49338314e362385bc560/native-states/non-git-onboarding-light.png), and [reviewed support information](../.artifacts/desktop-tests/3c3bf81b4a7b49338314e362385bc560/native-states/reviewed-support-light.png).

Earlier automated runs briefly opened a fixture trust dialog and an icon resource error. The owned test processes were stopped; no global dialog clicks were used. The harness now suppresses normal production startup, uses an isolated state directory and privately trusts only the fixture it created before exercising the access selector. Smoke and in-process test windows are offscreen and do not activate or enter the taskbar. Test exceptions are reported to console/files instead of a message box. Production launches retain normal windows and deliberate trust review.

The manual native folder picker, human trust decision, assistive-technology announcements, complete keyboard journey, actual high-contrast theme, 150/200 percent DPI, WebView2 terminal visuals and Git-beginner pilot remain release gates. Automated control interaction and captures do not replace them. The original review below records the pre-fix evidence and rationale.

## Scope and evidence

The review uses all four supplied concept images and fourteen client-area renders of the actual WPF application produced during this work session in `.artifacts/desktop-tests/d26d17238c0340efafade7b08b3a58c0`. Every image was opened and inspected. The fixture has two changed files and diverged local/team history with one commit on each side. Each captured page starts in Read-only. These images precede the desktop implementer's later stage/unstage placement, commit shortcut, output-state filter and operation-label improvements.

This is a visual review of those states, with a limited source check of accessible names, focus styles, graph labels and theme colors. It does not establish complete workflow usability, screen-reader behavior or accessibility compliance. The WPF captures show the application client area at 96 DPI, not the OS title bar, folder picker or live WebView2 terminal.

Concepts inspected: [folder selection](../archive/reference/01-project-folder.png), [status and next steps](../archive/reference/02-status-and-next-steps.png), [history and learning](../archive/reference/03-history-and-learning.png), [output and terminal](../archive/reference/04-output-and-terminal.png).

## Reviewed steps

| Step | Task and screenshot evidence | General health |
| --- | --- | --- |
| 1 | Understand the selected project in Overview: [light](../.artifacts/desktop-tests/d26d17238c0340efafade7b08b3a58c0/light-Overview.png), [dark](../.artifacts/desktop-tests/d26d17238c0340efafade7b08b3a58c0/dark-Overview.png) | Clear primary review action and separate file/download/upload counts. Permission and cached-data wording needs more explanation. |
| 2 | Review file contents in Changes: [light](../.artifacts/desktop-tests/d26d17238c0340efafade7b08b3a58c0/light-Changes.png), [dark](../.artifacts/desktop-tests/d26d17238c0340efafade7b08b3a58c0/dark-Changes.png) | Staged and unstaged groups are separate, including the empty staged group. Raw status codes add unnecessary difficulty. Action placement has since been changed and needs a new capture. |
| 3 | Understand recorded ancestry in History graph: [light](../.artifacts/desktop-tests/d26d17238c0340efafade7b08b3a58c0/light-History-graph.png), [dark](../.artifacts/desktop-tests/d26d17238c0340efafade7b08b3a58c0/dark-History-graph.png) | Two visible paths meet at a recorded common ancestor. Raw refs and a technical summary need beginner-facing labels. |
| 4 | Understand branches: [light](../.artifacts/desktop-tests/d26d17238c0340efafade7b08b3a58c0/light-Branches.png), [dark](../.artifacts/desktop-tests/d26d17238c0340efafade7b08b3a58c0/dark-Branches.png) | Current branch and base are explicit. The full base commit ID and remote HEAD alias are prominent before their meaning is explained. |
| 5 | Prepare a pull request: [light](../.artifacts/desktop-tests/d26d17238c0340efafade7b08b3a58c0/light-Pull-requests.png), [dark](../.artifacts/desktop-tests/d26d17238c0340efafade7b08b3a58c0/dark-Pull-requests.png) | Correctly distinguishes a pull request from Git pull and explains unavailable sign-in setup. Configuration language is technical for the intended audience. Authenticated behavior is outside this visual evidence. |
| 6 | Learn the daily journey: [light](../.artifacts/desktop-tests/d26d17238c0340efafade7b08b3a58c0/light-Learn.png), [dark](../.artifacts/desktop-tests/d26d17238c0340efafade7b08b3a58c0/dark-Learn.png) | Offline lessons, task search and a useful first journey are visible. Command inventory and coverage precede the beginner task list and could be less prominent. |
| 7 | Configure appearance and checks: [light](../.artifacts/desktop-tests/d26d17238c0340efafade7b08b3a58c0/light-Settings.png), [dark](../.artifacts/desktop-tests/d26d17238c0340efafade7b08b3a58c0/dark-Settings.png) | Explicit opt-in checks, interval and restart/session limits are explained. Executable paths and client IDs should eventually sit under a separate advanced section. |

All seven states keep the same project/branch/access context and the output dock. The screenshots do not show a blank or loading page. Some page content lies below the visible scroll area; this is expected scrollable content, not proof that it is inaccessible.

## Main findings and requested changes

### 1. Explain the current access mode where actions become unavailable

In every captured page, Read-only is selected and Check remote is disabled. The short “Remote not checked • cached refs” label does not explain how to unlock a check. Branch write buttons are also disabled without a visible nearby reason. Source provides a remote-check tooltip, but a hover-only explanation does not help keyboard users or people who do not try hovering over a disabled button.

Add a small, persistent mode explanation in the project header or relevant action region: “Read-only: inspect safely. Choose Monitor only to check for team updates. Choose Allow changes to save versions, update files or upload during this app session.” Keep the more detailed effect explanation in the deliberate access-change review. A recommendation may remain visible in Read-only; the explanation must distinguish reviewing from executing it.

### 2. Use readable file and graph labels before raw Git details

Changes currently displays `Tracked; index ., working file M` and `Untracked; index ?, working file ?`. History shows `HEAD -> refs/heads/main` and `refs/remotes/origin/HEAD`. Those labels ask the user to decode Git's internals before understanding a simple changed file or saved version.

Show “Modified — not selected for the next commit” and “New file — not yet saved in Git,” with a short “Selected for the next commit (staged)” explanation for the second group. Keep exact raw status in an optional detail or tooltip. History should show “Your branch: main” and “Team copy: origin/main,” while preserving exact refs in details and accessible text. Identify the common ancestor as the earlier shared version. Do not imply that lane colors always mean local/team ownership: the graph assigns lanes from actual ancestry, so labels must carry that meaning.

### 3. Give the output search an on-screen label

The output dock has an empty text box beside its channel selector in all fourteen renders. Source gives it an accessible name and tooltip, so this is a visible discoverability issue rather than a missing automation name. Add a visible “Filter output” label or concise field hint. The later output-state filter also needs an understandable visible selection.

The capture says “Initialize” for an inspected existing project, which could imply a repository change. The desktop implementer has already corrected the source operation label to Inspect. Fresh captures should verify this before closure.

### 4. Correct normal-text contrast in the dark primary action

The dark primary “Review changed files” button uses white text on `#3C93F2`. Relative-luminance calculation yields **3.16:1**, below the 4.5:1 target for normal-sized text. Use dark text such as `#17243C` on that fill (**4.91:1**), or choose a darker fill with a passing white-text ratio. Preserve the system high-contrast foreground override.

Light muted text uses `#64748B` on `#F6F8FC`: **4.48:1**, just below 4.5:1. Slightly darken the muted color and verify each affected background. Light primary white text on `#1769E0` is **5.08:1**. Dark muted text on the window/surface colors is above 7:1. These calculations cover the inspected palette combinations; they are not a whole-application contrast certificate.

### 5. Reduce advanced setup language in the first daily experience

The Learn page leads with learning version, “133 declared core commands,” and installed inventory/coverage. Pull requests and Settings expose a registered GitHub App public client ID and Git executable path. These may be necessary setup controls, but command coverage and deployment-validation prose are maintainer information. Keep the daily journey/search prominent and group technical configuration under a plainly labeled Advanced section in a later polish pass. For the immediate pilot, the higher-value permission and file-label fixes come first.

## Screenshot excerpts

Overview has the intended separate counts and primary review action, with the permission/freshness explanation gap visible at the top.

![Actual light Overview](../.artifacts/desktop-tests/d26d17238c0340efafade7b08b3a58c0/light-Overview.png)

Changes separates file groups but exposes raw status codes. This image precedes the later action-placement improvement.

![Actual light Changes](../.artifacts/desktop-tests/d26d17238c0340efafade7b08b3a58c0/light-Changes.png)

History uses real parent paths; the ref labels and relationship summary are still technical.

![Actual dark History](../.artifacts/desktop-tests/d26d17238c0340efafade7b08b3a58c0/dark-History-graph.png)

## Source-backed accessibility strengths and limits

The source gives project/access selectors, output filters, dynamically created form fields and file lists accessible names. The graph has an adjacent keyboard-selectable commit list whose item automation names include exact commit IDs and parent IDs. Focus triggers exist for buttons, list items, tabs and selectors. Theme resources include a system high-contrast override. These are useful implementation provisions, not substitutes for testing their behavior with assistive technology.

The captures alone do not verify focus order, visible focus during keyboard use, announcements after changing a plan, screen-reader reading order, keyboard access to the splitter/terminal, high-contrast rendering, or layout at 150/200 percent DPI. Confirm these on a supported Windows machine. Also capture first-run/no-project, non-Git onboarding, selected diff, operation preview, busy progress, actionable failure, Problems/support review and interactive terminal states before claiming their visual quality is complete.

## Pilot gates

Ask representative Git beginners to open a disposable project and explain the displayed mode, changed files, local saved versions, team-copy freshness and the next action in their own words. Observe them selecting one file, reviewing contents, saving a local commit, checking team changes and understanding a rejected upload. Success must be measured from these tasks; the review does not claim that descriptive text alone resolves confusion.

The immediate implementation handoff is mode help, readable file/ref labels, visible output search and contrast. Reinspect new WPF captures after those changes, then run the keyboard/scaling and human pilot checks as explicit release gates.
