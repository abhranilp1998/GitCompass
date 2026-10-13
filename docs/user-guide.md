# Getting started with GitCompass

## Start the portable app

Extract the whole `GitCompass-0.1.1-win-x64.zip` into a folder you can write to. Run `GitCompass.exe` with its accompanying files in that folder. This internal trial is unsigned. It includes its .NET runtime; you do not need Visual Studio, VS Code or a .NET SDK to use it. The original 0.1.0 trial remains a separate older copy.

Git for Windows must be installed. If GitCompass cannot find it, use the official [Git download](https://git-scm.com/downloads/win), then restart or set its actual `git.exe` path in Settings. The embedded terminal also needs Microsoft's [WebView2 Evergreen runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/). An offline prerequisite install needs the full Standalone Installer, as explained in the [runtime distribution guide](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution). GitCompass shows detected prerequisite problems and does not silently replace these tools.

Start with **Learn**, especially the explanations of changed, staged, committed and published work, then try a disposable project folder in Read-only. The initial delivery is Windows x64; acceptance on your team's specific OS versions remains to be recorded. Your settings, recent projects, redacted logs and protected GitHub credentials are kept under your Windows local application-data directory in `GitCompass`, separately from project files. Do not share the credentials directory.

For this workspace, a disposable practice repository is already prepared at `F:\Dev\Projects\GitCompass\artifacts\GitCompass-Practice` (`artifacts\GitCompass-Practice` relative to the source workspace). Choose that folder, keep Read-only selected, and inspect modified `welcome.txt`, new `ideas.txt` and the one locally saved history entry. It has no remote or upload destination. The practice folder is separate from the application ZIP; another source checkout can create its own copy with `powershell -ExecutionPolicy Bypass -File scripts/create-practice.ps1`. That script refuses to replace an existing practice folder.

## Open and understand a project

Choose **Select project folder** and confirm the actual project root. Opening a folder inspects it in **Read-only**. A subfolder may belong to a repository higher up; both locations are shown when relevant. A folder without Git gets an explained initialization path; initialization is a separate deliberate action.

Read the three facts separately: changed files are work in your folder, incoming commits are versions in fetched team history, and outgoing commits are versions saved locally that are not in that fetched history. Unknown counts mean no reliable comparison is available. Cached remote data may be old even when the local file inspection just completed.

**Check remote (Fetch)** refreshes knowledge of the team history. **Update this branch** changes your branch and files to integrate reviewed team work. **Save a local version (Commit)** records the staged contents on your machine. **Upload committed work (Push)** publishes commits to the displayed destination. A push does not upload unstaged changes, ignored files, ordinary stashes, or unsaved editor buffers.

## Permissions

- **Read-only:** local inspection; no GitCompass fetch, writes, or shell.
- **Monitor only:** permitted fetch and hosted reads after setup; no commit/update/push or shell.
- **Allow changes:** reviewed operations and real interactive PowerShell for this app session.

View branch names, history, comparisons and diffs in Read-only or Monitor only. In **Branches**, select a row and choose **View selected branch history**. This does not switch branches or change checked-out files. **Review switching to selected local branch** is a separate action requiring Allow changes. Fetched remote branches show cached information, not a live server view.

For a folder without Git, the header says **Not using Git**. Git pages offer learning and **Review ways to start using Git**; opening those pages does not initialize the folder. Remote checks and periodic monitoring are unavailable until a working Git repository exists. A Git project without a remote or GitHub account can still be used for local commits and history.

Selecting a more restrictive mode revokes queued actions immediately. If an app-owned operation or shell is active, the mode transition remains pending until that activity has ended. Permission mode is separate from trusting the repository's hooks, filters and configured transport helpers. Allow changes never automatically runs a Git action.

## A daily workflow

1. Open the project and verify its branch and access mode. Save files in your usual editor.
2. If desired, enable Monitor only and check remote. Read cached/fresh labels.
3. Review changed files and staged contents. Stage only the contents intended for the next version.
4. Enable Allow changes, enter a meaningful commit message, review the preview, and save the version locally.
5. Check remote and read the new plan. Committing while behind may create divergence that needs integration review.
6. Review the displayed destination and outgoing history before a normal push.
7. Verify the result, then lock back to Monitor only or Read-only.

If the histories diverge, review both paths from their common ancestor. Preserve local edits before integration. A conflicted merge stops normal synchronization. Use **Reveal file in Explorer**, then choose your usual editor to review the affected file. GitCompass reveals the file instead of running a script or executable from the project. Edit the intended resolution, review and stage completed paths, then review Continue merge. Abort is operation-specific recovery, not a universal backup.

Ignoring a file prevents new untracked contents from being added by ordinary staging. It does not remove already tracked files or old copies from history. Private configuration and possible credentials need their own review. Keep original ZIP copies while migrating; do not merge unrelated histories automatically.

## Output, terminal, and help

Git Output records app-managed activity by project and operation. Problems explains actionable failures. Terminal is a real shell and can run arbitrary commands after Allow changes. Its tabs retain their starting project when you switch projects; a shell can later change directory independently. Review multiline paste before sending it.

Expand an operation group to read its retained output or use **Copy this operation**. Channel, text and state filters help find a particular check or failure; clearing the visible output does not cancel work. Truncation labels mean earlier visible detail was omitted. A copied operation is bounded and may also omit earlier output; the reviewed support bundle is a separate choice.

History graph can compare two selected local or fetched branches. The comparison names the exact recorded versions and shows counts only on each side. Filtering searches the loaded page's message, author, label and ID; it is not a repository-wide history search. Dotted edges can continue off the loaded page or to a parent hidden by the filter, and do not invent a new relationship.

Learn contains original local lessons and reference entries. No AI or online chat is needed for status, history, guidance, or lessons. Pull requests are hosted review requests and differ from Git pull; GitHub API features require separate setup and sign-in.

Prepare support view shows a redacted summary to review before saving locally or sharing yourself. Source files, diffs, environment dumps and terminal scrollback are excluded. Redaction is best effort. GitCompass never automatically sends diagnostics.

## First publication or a ZIP working copy

If the folder has no repository, keep a dated backup first. Review the initialization folder and initial branch, then explicitly enable Allow changes and initialize. Review ignore suggestions and files, enter your own commit author name/email, select intended contents, inspect staged work and save a local commit. Author identity is separate from signing in.

Use **Remote and author setup** to enter an authorized existing remote and publication branch. Review the initial branch push. A successful result must confirm the published commit at the displayed branch. Verify a separate clean clone before retiring the old working copy or backup. The first commit records the selected current snapshot; ZIP dates do not create genuine historical Git commits.

If the team already has a canonical repository, clone it into a separate empty folder. Preserve the ZIP copy, compare its files with the clone, and copy only reviewed working changes into the clone. Exclude Git metadata, private configuration and generated files. Review, stage, commit and upload those changes using the team's branch rules. Never copy an old `.git` directory over the new clone or automatically combine unrelated histories.

## GitHub pull requests

The administrator supplies a registered GitHub App's public client ID and enables [device flow](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-user-access-token-for-a-github-app). The app needs Pull requests write and Contents read on the chosen repositories; Checks read enables private check-run display. Put the public client ID in Settings; select **Sign in to GitHub API** and complete authorization in the system browser. GitCompass does not collect your GitHub password. Git fetch/push uses your separate Git credential-helper or SSH setup.

Publish the source commit first. On Pull requests, review the exact source repository/branch and target repository/branch, title, description and draft choice. The app verifies that the source commit is published before submission. If the result becomes uncertain, inspect the matching request on GitHub before starting another submission. Pending mergeability means GitHub has not supplied a known result. This build has controlled-response tests; live sign-in and submission still need a designated sandbox acceptance run.

## When an action fails

Read Problems and the matching operation output. A rejected push keeps local commits; check team history and rebuild the plan. A changed file, index or branch can invalidate an earlier review; reopen the review using current facts. A canceled process may have made progress, so inspect an Outcome unknown before repeating a write. Do not delete arbitrary locks or reset files as a general fix.

Keep app-created stashes until their restored contents have been reviewed. Included untracked files and ignored files are different; ignored files are excluded from the stash choice. A restoration conflict needs deliberate editing and inspection. Advanced rebase/cherry-pick/worktree/submodule recovery may require your usual editor or a developer-guided terminal procedure.

Closing GitCompass fully stops monitoring and closes its owned sessions after the exit review. Existing terminal tabs retain their originating project when you switch the selected project. External editors and Git tools can still modify repository state independently of GitCompass's access selector.

Team-update notices can be muted or given local-time quiet hours in Settings. Those choices suppress notices while permitted checks continue; they do not grant fetch permission or hide status/operation failures. A notification opens the affected project's plan and never runs an update automatically.
