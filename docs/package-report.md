# GitCompass package identity

## Current trial: 0.1.1, 13 October 2026

The separate **unsigned Windows x64 internal trial** adds branch-history viewing in Read-only and Monitor only, ordinary-folder guidance, clearer disabled terminal controls, and suppression of implicit partial-clone downloads during inspection. See [the inspection update record](../archive/docs/inspection-update-0.1.1.md) for behavior and the executed source checks. This report remains outside the portable ZIP because it contains that ZIP's final checksum.

| Portable artifact | Verified value |
| --- | --- |
| File | [GitCompass-0.1.1-win-x64.zip](../artifacts/GitCompass-0.1.1-win-x64.zip) |
| Bytes | **73,787,577** |
| SHA256 | `AD99646312B91AE736F1FB94E02AF952E6DC75334E37601E0B2D3F10FA626478` |
| Checksum companion | [GitCompass-0.1.1-win-x64.zip.sha256](../artifacts/GitCompass-0.1.1-win-x64.zip.sha256) |
| Manifest | **471** listed content files, each with byte length and SHA256; `package-manifest.json` is separate. |
| Runtime | Self-contained `win-x64`; .NET/WPF **10.0.12** bundled. |
| Application directory | [GitCompass-0.1.1-win-x64](../artifacts/GitCompass-0.1.1-win-x64) |
| Detailed manual | Document version **1.1**, application baseline **0.1.1**; 42-page PDF and self-contained HTML included. |

The final ZIP passed checksum, per-file size/SHA256 and no-unlisted-content checks, the Windows x64 PE-header check and bundled-framework declarations. Its actual extracted executable passed **first-run** and **missing-Git** startup checks with development runtime environment variables removed. Evidence: `.artifacts/inspection-update-package-verification.log` and `.artifacts/package-check-a6675ee493214c949e544d22ad87d031/verification.json`.

The published executable passed **14 actual page/theme renders**, all seven pages in light/dark, with the disposable repositories' working files, index, refs, objects and configuration fingerprints unchanged in Read-only. Evidence: `.artifacts/inspection-update-package-renders.log` and `.artifacts/desktop-tests/bc72931255a44307b2562453809eb1db`. The final extracted executable, runtime and assets match the staging copy used for those renders; the post-package comparison verifies **443 non-documentation payload files**, including terminal HTML and component notices, in `.artifacts/inspection-update-package-payload-parity.json`. Framework-dependent source test builds are separate RID outputs; their assembly hashes are not claimed to match the self-contained publish.

The solution Release build and source verification passed the recorded **64 domain assertions, 103 real-Git assertions, 30 adapter checks, 38 native UI interactions, 11 stage/commit workflow checks, 27 focused Git assertions, three startup/close scenarios, 24 ConPTY checks, five offline WebView2 checks and seven installed-command checks**. The update record identifies the successful logs and retains the initial UI test timing failure separately. All tests used disposable local fixtures and controlled hosted responses. No production project, live GitHub push or PR submission was used.

To try this copy, close the older app yourself and run `GitCompass.exe` from the new full versioned directory. On another machine, extract the entire new ZIP first. Start in Read-only with a disposable or prepared practice repository, open **Branches**, select a row and choose **View selected branch history**. For an ordinary folder, verify **Not using Git** and the setup/learning explanation. Opening that folder or the setup explanation does not initialize Git. Installed Git for Windows remains required for repository inspection; WebView2 Evergreen is needed for a real terminal. No IDE or development SDK is required to run the portable app.

The new [GitCompass-0.1.1-source.zip](../artifacts/GitCompass-0.1.1-source.zip) has its own [checksum companion](../artifacts/GitCompass-0.1.1-source.zip.sha256). Its packaging script verifies every selected source/document/reference entry. The source ZIP's checksum is not embedded here because this report is included in that archive.

The two original 0.1.0 archives retain the exact SHA256 values recorded below, and the original `GitCompass-win-x64` application directory was preserved. No persistent User/Machine environment variables or PATH entries were changed for this update. The new `GIT_NO_LAZY_FETCH=1` safeguard is applied only to managed inspection child processes. Production signing, live GitHub App, fresh-machine, company OS, accessibility and beginner acceptance gates remain open.

## Original 0.1.0 package identity

The Windows x64 portable build is an **unsigned internal trial**. Its runnable implementation and executed checks are recorded in [verification.md](verification.md); [task-handoff.md](../archive/docs/task-handoff.md) reconciles the original and resumed departments. This report stays outside the portable ZIP so the ZIP checksum cannot depend on a document containing its own checksum.

| Portable artifact | Recorded value |
| --- | --- |
| File | [GitCompass-0.1.0-win-x64.zip](../artifacts/GitCompass-0.1.0-win-x64.zip) |
| Bytes | **72,747,478** |
| SHA256 | `0099325C91C8A5FEF6FA92444D4FB983309ED9552EC94A62CE93B43F40910E09` |
| Checksum companion | [GitCompass-0.1.0-win-x64.zip.sha256](../artifacts/GitCompass-0.1.0-win-x64.zip.sha256) |
| Manifest | **464** listed content files, each with byte length and SHA256; `package-manifest.json` is the separate manifest file. |
| Runtime | Self-contained `win-x64`; .NET/WPF **10.0.12** bundled. |
| Application directory | [GitCompass-win-x64](../artifacts/GitCompass-win-x64) |

Extract the **whole ZIP** and run `GitCompass.exe` beside its DLLs, assets and terminal folder. No development SDK, Visual Studio, VS Code or Node.js is needed to run it. Installed Git for Windows and WebView2 Evergreen remain prerequisites. Start with Learn, then inspect the separate prepared [GitCompass-Practice](../artifacts/GitCompass-Practice) folder in Read-only; it has one local commit, one modified file, one new file and no remote. The practice folder is not inside the application ZIP.

## Executed package checks

The final ZIP passed checksum, manifest sizes/per-file SHA256/no-unlisted-content checks, x64 PE header and bundled-framework declaration checks. Its actual extracted executable rendered and exited cleanly for **first-run** and **missing Git**, with development runtime environment variables removed. The final report is `.artifacts/package-verification-final.log`; extraction and captures are under `.artifacts/package-check-f2fc552da22e4063910e0e566aba0f47`.

The published application also passed **14 actual portable page/theme scenarios**, all seven pages in light/dark, with worktree/index/refs/objects/config content fingerprints unchanged in Read-only. Their log is `.artifacts/package-render-final.log`; captures are under `.artifacts/desktop-tests/e0c49f719bcf4cdc9224fdd051bede18`. Documentation was recopied after those renders; the final ZIP startup/integrity checks were then rerun. `.artifacts/package-payload-parity-final.json` confirms **442 non-documentation payload files** in the final ZIP are byte-identical to the package used for those fourteen renders, including assemblies, bundled runtimes, terminal/learning assets, JSON and component license files. The current Windows user token was non-elevated. This evidence does not establish a fresh-user/machine installation or the company-supported OS matrix.

The package retains full .NET, WPF, MVVM Toolkit, WebView2 SDK, Windows SDK and xterm licenses/notices, local lesson/terminal assets, `dependencies.json`, user guide, pilot checklist and the documentation tree. The original Windows SDK license is preserved unchanged; [third-party notices](../THIRD_PARTY_NOTICES.md) record its official source and hash.

## Source handoff

The companion [GitCompass-0.1.0-source.zip](../artifacts/GitCompass-0.1.0-source.zip) has a separate [SHA256 companion](../artifacts/GitCompass-0.1.0-source.zip.sha256). The source checksum is deliberately not embedded here because this report is included in that archive. `scripts/package-source.ps1` writes the selected source/reference/documentation/assets/scripts and a per-file `source-manifest.json`, then checks every archived entry's size and SHA256. Development tool caches, `.git`, test fixtures and generated build outputs are excluded.

The original build-pack specification and four UI concepts remain in the source archive's `archive/reference/`. Use [development-environment.md](development-environment.md) to open/build the solution and [user-guide.md](user-guide.md) for the beginner workflow.

## Acceptance still open

This is a runnable internal handoff, not production-release acceptance. [requirements.md](requirements.md) accounts for all 20 FR IDs and [acceptance.md](acceptance.md) all 38 original scenarios. Human beginner tasks, keyboard/screen reader/high contrast/DPI, fresh-user install/update/OS coverage, live designated GitHub App validation and publisher signing/distribution decisions remain open. The matrices also retain the unexecuted post-server-interruption, scheduler recovery/dedup timing and visible-output latency variants. M5 and M7 remain open; no all-P0/P1 or production-ready claim is made.
