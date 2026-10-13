# GitCompass 0.1.1 - branch inspection and ordinary-folder support

13 October 2026. This is a separate unsigned Windows x64 internal trial. The original 0.1.0 archives remain historical artifacts. Production signing, live GitHub, fresh-machine, accessibility and beginner acceptance gates remain open.

## Viewing a branch and switching a branch

Branch names, locally available recorded history, comparisons and diffs can be inspected in **Read-only** and **Monitor only**. Selecting a branch row does not switch the checkout. **View selected branch history** opens only that exact branch tip's ancestry in History graph and states which branch remains checked out. It works for local branches and fetched remote copies. A fetched copy is cached information from an earlier check, not a live view of the server.

**Review switching to selected local branch**, creating a branch, committing, integration, uploading and a real PowerShell terminal require **Allow changes**. Switching can change the working files and index, so it remains a separately reviewed operation. See [Git's switch reference](https://git-scm.com/docs/git-switch).

Monitor only adds permitted remote checks. Fetch writes local Git objects and remote-tracking metadata, while leaving the current local branch and working files unintegrated. That is why fetch is separate from strict Read-only. See [Git's fetch reference](https://git-scm.com/docs/git-fetch).

The terminal's **New PowerShell session** button is disabled when access does not permit it. Its explanation points to Branches and History graph for inspection. The underlying permission checks remain in place.

## Projects without Git or GitHub

An ordinary folder is recognized without creating `.git` or editing its files. The header shows **Not using Git**. Changes, History graph, Branches and Pull requests explain why Git information is unavailable and offer learning, setup review and another folder selection. **Check remote** and periodic monitoring are disabled for that folder, including in Monitor only. Starting local history or cloning still requires deliberate Allow changes and a reviewed operation.

A local Git project without a remote or GitHub account still supports local versions, branches and diffs. Online connection setup is optional. GitCompass does not provide general file editing or automatic backup for folders outside Git.

## Inspection of incomplete repositories

The managed Git process disables optional index locks. It now also sets `GIT_NO_LAZY_FETCH=1` for inspection, so reading a missing partial-clone object cannot silently fetch that object. Incomplete content is reported as unavailable. Explicit permitted remote operations remain separate. These are child-process environment settings, not persistent User/Machine variables or PATH changes. See [Git's process and environment reference](https://git-scm.com/docs/git).

## Validation and handoff

The following checks were executed on 13 October 2026. All test repositories and remotes are disposable workspace fixtures; no real project or live GitHub mutation was part of these checks. Counts describe executable assertions and scenarios, not closed production-acceptance gates.

| Check | Executed result | Evidence under `.artifacts` |
| --- | --- | --- |
| Full solution Release build | Zero warnings and zero errors | `inspection-update-native-rerun.log` |
| Domain/access rules | 64 assertions passed | `inspection-update-build.log`, before the initial UI timing failure |
| Real Git integration | 103 assertions passed, including the three partial-clone inspection checks | `inspection-update-build.log`, before the initial UI timing failure |
| Local/GitHub adapters | 30 checks passed | `inspection-update-build.log`, before the initial UI timing failure |
| Source WPF pages/themes | 14 scenarios passed | `inspection-update-build.log` |
| Published WPF pages/themes | 14 scenarios passed, all seven pages in both themes, with Read-only repository content fingerprints unchanged | `inspection-update-package-renders.log` |
| Native WPF interactions | 38 checks passed, including branch viewing in both modes and four non-Git page states | `inspection-update-native-rerun.log` |
| Source startup/close | Three scenarios passed | `inspection-update-remaining-checks.log` |
| Native stage/commit workflow | 11 checks passed | `inspection-update-remaining-checks.log` |
| Focused real-Git safety | 27 assertions passed | `inspection-update-remaining-checks.log` |
| Native ConPTY | 24 checks passed | `inspection-update-remaining-checks.log` |
| Offline WebView2 renderer | Five checks passed | `inspection-update-remaining-checks.log` |
| Installed-command inventory | Seven checks passed | `inspection-update-remaining-checks.log` |
| Manual content and layout | 42 rendered pages, 38 chapters, 33 tables, three screenshots and two diagrams; all source text, contents destinations and page bounds checked | `../output/pdf/functional-documentation-layout-validation-0.1.1.json` |

The initial UI test attempted another operation before the previous asynchronous refresh finished; the app correctly blocked it. The successful native rerun waits for the host's idle state. The access selector also ignores stale completions after a project/access-generation change. The initial failed run remains in its log instead of being presented as a passing complete suite.

The final ZIP's identity, actual published-page renders, extracted startup checks and retained original archive checksums are recorded in the source checkout's `docs/package-report.md`. That report is kept outside the portable ZIP to avoid a checksum depending on its own recorded value. Published-page captures for this build are under `.artifacts/desktop-tests/bc72931255a44307b2562453809eb1db`.

The updated executable belongs in `artifacts/GitCompass-0.1.1-win-x64/GitCompass.exe`, with its full directory. Its ZIP is `artifacts/GitCompass-0.1.1-win-x64.zip`. Extract the entire ZIP before use. The scripts derive package versions from the built assembly and use a distinct versioned directory. The updated manual is application baseline 0.1.1/document version 1.1; the earlier 0.1.0 reading copies remain available separately.
