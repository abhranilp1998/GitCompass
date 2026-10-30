# Beginner walkthrough and Windows 11 Fluent update

13 October 2026. This is a source-development update following the [beginner audit](../archive/docs/beginner-experience-review-2026-10-13.md) and [walkthrough proposal](beginner-walkthrough-proposal.md). The existing 0.1.1 portable ZIP and exported manual are earlier artifacts; they have not been regenerated for this change.

## Try the current build

From the source workspace:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -ExecutionPolicy Bypass -File scripts/run.ps1 -Development
```

`-Development` deliberately runs the current source instead of an existing portable package. Git for Windows and the pinned .NET SDK are development prerequisites. Start with **Start the walkthrough** or **Practice with a sample project**. Choose a parent outside an existing Git project for the sample; GitCompass creates a new separate folder and refuses to create it inside an existing repository.

## What changed

The WPF application now uses native .NET Fluent control styles with a Windows 11 Settings layout: a consistent sidebar, page search, breadcrumbs, neutral grouped cards, Segoe typography and Windows icons. Pages use Home, Projects, Changed files, Saved versions, Branches, Sharing, Learn and Settings. Activity, problems and terminal tools are available in an expandable panel. System is the default appearance, with Light and Dark choices, system accent colors and a high-contrast palette. Appearance choices save immediately; other preferences retain their explicit Save settings action.

The optional walkthrough can be paused and resumed. It explains the difference between saving an editor file, recording selected contents as a local Git version, and uploading versions. Completed sections collapse; progress comes from the actual selected project and successful reviewed operations. The guide records the real commit ID after a successful save, and never counts downloaded or pre-existing history as the user's first save. Resuming inspects the project again and starts with Read-only access.

Ordinary folders now ask whether these are the user's own files or a team copy before recommending initialization. Author name and email are checked before a commit and configured through the existing reviewed, repository-local operation. Changed files have explicit checkboxes, wrapped labels and expandable contents. The new offline introduction explains the basic workflow without requiring command knowledge. Git installation guidance retains the selected folder and can recheck and continue.

Git inspection now distinguishes local-only projects from projects with remote connections. A local project no longer receives repeated advice to fetch a nonexistent remote. Team checks and monitoring require a real remote and tracking setup; choosing an upload or team-update goal still asks for the connection it needs.

## Five project situations

| Situation | Current behavior | Practical limit |
| --- | --- | --- |
| Own files, no Git | Intent choice, explicit access/trust, reviewed initialization on `main`, author setup, file selection and local save | Users still review exclusions and private files before saving. |
| Team link, ZIP or Drive copy | Preserve the original; enter the team link and choose a separate empty destination for a reviewed download | Transferring edited files remains a deliberate manual comparison; no automatic copy or invented ZIP history. |
| Existing local Git project | Explain local history, offer selected-file saving and optional sharing | Creating a hosted repository and transport credentials remain separate setup work. |
| Connected team project | Keep the existing state-based check, update, integration, upload and publication review engine | Live GitHub sign-in/submission needs sandbox acceptance. |
| A problem needs attention | Missing-author remedy, permission explanation, Git recheck, actionable Problems and reviewed output | Complex conflicts and unsupported recovery still need the user's editor or developer help. |

The three access states and trust model are preserved: Read-only inspection by default; permitted remote checks under Monitor only; reviewed changes and terminals after a manual Allow changes unlock. Opening a folder or advancing a tutorial never runs a write. A successful local commit explicitly says that it has not uploaded the work. The author identity form does not modify global Git configuration.

## Checkpoints

The original workspace is committed locally on `main` as **`1f3087f`**, tagged **`baseline-before-fluent-redesign`**. All 152 staged files were compared with their original byte content before that commit. `.tools`, build output and test fixtures are excluded. No remote is configured for this workspace.

The completed source update is saved as a separate local checkpoint, tagged **`beginner-fluent-checkpoint`**. If you later choose to undo that update from a clean working tree, `git revert beginner-fluent-checkpoint` creates a new reversal commit and preserves the earlier history. Build outputs are excluded from Git, so rebuild the source before running a reverted application. Keep any later work committed or separately preserved before changing versions.

## Verification

The solution builds with zero warnings and errors. Backend validation passed 73 domain checks and 108 real-Git integration checks; 27 focused Git regression checks also passed. The final adapter suite passed 31 checks, including 400 concurrent settings reads during 40 atomic saves, with valid complete snapshots and the exact final theme retained. Native desktop verification passed 14 Light/Dark page renders and 38 interaction checks, including Read-only repository fingerprints, project intent, operation review/cancel, themes, settings and permission behavior.

The final native workflow suite passed **52 checks**, including reviewed ordinary-folder initialization, repository-local author setup, one-checkbox staging, actual first-commit contents, unselected-file preservation, team-copy intent, pause/restart resume, fresh-session Read-only, later-history completion, practice isolation, search, activity controls, the real Fluent dropdown popup, and automatic Dark/Light/System persistence. The focused appearance regression also passed five checks. The final 38 UI interaction checks were rerun after the storage fix.

Native walkthrough screenshots are in [the final evidence folder](F:/Dev/Projects/GitCompass/.artifacts/native-beginner/976a7086e70147cd86535e9245f495e3/05-first-version-complete-dark.png), with nine captured states. Final UI interaction evidence is under `.artifacts/desktop-tests/1da07f0fbc0f4c3fac7e485e67dd4171/native-states/`. These tests use an isolated WPF host; the normal executable captures below separately show application branding.

The theme regression exposed a Windows file-sharing bug: an asynchronous settings reader could prevent the atomic save from replacing its pathname. Readers now allow delete sharing, so they retain their original snapshot while a new settings file replaces the name. Storage awaits also finish independently of the UI synchronization context. Under heavier reader contention, Windows can still deny replacement while acquiring destination access, as described in [Microsoft's File.Move documentation](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move?view=net-10.0). Replacement retries only Windows access/sharing errors, with at most six delays totaling 620 ms, using the same fully written temporary file and honoring cancellation. The saved destination is never deleted or rewritten in place. Permanent errors propagate and preserve the earlier file. The 31 adapter checks and five native appearance checks passed after this final hardening.

Visual evidence is held in `.artifacts/fluent-preview-20261013/`; its capture manifest records the tested assembly hashes and unchanged fixture fingerprints. These are actual WPF renders, not illustrative mockups. Screenshots demonstrate the inspected layouts; they do not establish representative novice usability or physical-device acceptance. A novice pilot, live hosted-service acceptance, high-contrast/theme transitions on the target Windows installation, and distribution/signing remain release checks.

## Maintenance

The shell retains the original application and infrastructure services. Shared native Fluent styles live in `App.xaml`; navigation and activity layout live in `ShellWindow.xaml`. Beginner-specific UI is separated into `ShellWindow.Beginner.cs`, and its progress is stored independently in `beginner-guide.json` under the app state directory. Corrupt progress is preserved rather than silently replaced. Progress, theme preferences and recent folders never restore write permission after restart.
