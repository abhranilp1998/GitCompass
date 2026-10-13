# GitCompass implementation and verification

## 13 October 2026 beginner and Fluent source update

The [beginner walkthrough and Fluent verification record](beginner-fluent-update.md) covers the latest source changes and native walkthrough checks. The portable-package evidence below belongs to the earlier inspection build; that package has not been regenerated for the new walkthrough.

## 13 October 2026 inspection update: 0.1.1

The separate 0.1.1 trial adds explicit branch-history viewing in Read-only and Monitor only, truthful guidance for folders without Git, disabled terminal/remote controls where access or repository setup does not permit them, and suppression of implicit partial-clone fetch during inspection. The [inspection update record](inspection-update-0.1.1.md) contains current executed results, including **103 real-Git assertions** and **38 native UI checks**. The source checkout's `docs/package-report.md`, kept outside the portable ZIP, records the new versioned application archive and its checks.

The remaining sections below retain the original **0.1.0 verification baseline** from 12 October. Historical counts and package identities are not evidence by themselves for the later build. The update record and package report distinguish the current reruns from that original baseline.

The original build pack is preserved unchanged in `reference/`; [specification.md](specification.md) records its role and the GitCompass naming decision. This record distinguishes a runnable implementation, executed automated assertions, and manual/external acceptance. The internal portable build is not a signed production release and does not by itself close all P0/P1 gates.

## Original 0.1.0 environment snapshot

12 October 2026: Windows x64 build 26300; Git for Windows 2.54.0.windows.1; workspace-local and system .NET SDK 10.0.401 with runtime 10.0.12. Visual Studio Professional 2026 18.10.3 and the .NET desktop workload are now installed; [development-environment.md](development-environment.md) records the observed installation paths and process environment scope. WebView2 runtime detected as 154.0.4258.62. The workspace SDK ZIP SHA512 was verified against Microsoft's official release metadata before extraction. WPF/terminal package versions and lockfiles are pinned.

## Recorded checks

The final full solution Release build succeeded with **zero warnings and zero errors** in `.artifacts/solution-build-final.log`. Completed source-suite results follow. Produced portable-package checks are recorded separately below.

| Executed check | Recorded result | What it establishes |
| --- | --- | --- |
| Domain advice/access executable | 64 assertions passed in `.artifacts/core-domain-current.log`. | Mixed-state priority, unknown facts, goals/intent, truthful Inspect capability, grant revocation/pending downgrade/restart and atomic shared-worktree exclusion. |
| Real Git integration executable | 100 assertions passed in `.artifacts/core-integration-clean.log`. | Actual candidate/init/identity, exact staging/unstaging/commit, Read-only full content invariance/helper suppression, stale preview/grant denial, verified initial publication/clone, protected fetch mappings, dirty update/stash/exact fast-forward, ancestry/selected-branch comparison/anchor pagination, divergence/two-parent merge, branch actions/server race, text conflict/abort/continue, actual push URL rewrite, retarget rejection, preserved ignores, hook-changed contents/branch outcome uncertainty, ignored collision preservation and nested/worktree/bare discovery. |
| Focused Git safety executable | 27 assertions passed in `.artifacts/core-focused-verified.log`. | Scoped tracking-ref pruning, stale outgoing/server evidence, direct reviewed push URL preserving local/tag namespaces, second URL-rewrite rejection without config mutation, failed-fetch freshness despite unchanged byte fingerprint, removed upstream staying unknown, live pre-exit output, canceled work preservation, bounded inherited-pipe/owned-descendant shutdown/lease release and actual stash restoration conflict preserving the exact stash/untracked contents while blocking publication. |
| Local/GitHub adapter executable | 30 checks passed in `.artifacts/adapters-final.log`. | Atomic settings/recent dedup/corruption preservation, quiet hours/mute settings, current-user DPAPI/no plaintext token, reviewed redacted local save, hosted permissions, fixed-origin pagination/per-page conditional continuation, PR/review/check/status and restricted-info handling, explicit source SHA verification, rate/inaccessible/revoked states, uncertain matching PR/no blind resubmit, device polling/slow-down and secret-free rotation. |
| Actual native stage/commit workflow | 11 checks passed in `.artifacts/desktop-workflow-final.log`. | Isolated host opens the real repository Read-only, preserves index on inspection, explains disabled stage, enables correct session, reviews/executes the exact selected file, creates an actual local commit with no upload, refreshes real history and locks back. |
| Actual WPF page/theme renders | 14 scenarios passed in `.artifacts/ui-final.log`. | All seven pages render in light/dark; real two-clone divergence fixture; worktree/index/refs/objects/config fingerprints unchanged in Read-only. |
| Actual WPF interactions | 23 checks passed in `.artifacts/ui-final.log`. | Branch relation counts and loaded-page history filtering; per-operation/channel/problem output groups and copy controls; hosted stale/denied results; safe Explorer-only project-file reveal; selected-file review/cancel/error recovery; settings persistence; collapsed technical setup; reviewed palette; project-isolated failures and editable support artifacts. |
| Actual executable startup/close regressions | Three scenarios passed in `.artifacts/desktop-startup-final.log`. | Real source-built first-run and missing-Git executables render offscreen and exit cleanly; repeated native close requests wait for cancellation and complete exactly one deferred close without reentrancy. |
| Native ConPTY executable | 24 checks passed in `.artifacts/terminal-native-final.log`; see `tests/GitCompass.Terminal.Tests/README.md`. | Two actual PowerShell sessions, independent I/O, resize 120×40, interrupt/reuse, bounded large output, paste, revoked input, owned descendant shutdown, failed/immediate startup and shared-metadata gate. |
| Offline WebView2 renderer | Five checks passed in `.artifacts/terminal-renderer-final.log`. | Actual local xterm rendering, inert script/link output and origin/schema/navigation restrictions. |
| Offline installed-command inventory | Seven checks passed in `.artifacts/inventory-final.log`; [learning-verification.md](learning-verification.md). | 133 unique declared command entries and 138 lessons; real offline help inventory; separate documentation topics and explicit unsupported/missing catalog entries. |

These are executable assertion counts, not counts of fully closed release scenarios. Test repositories and local bare remotes live under `.artifacts` and contain disposable data. The full source test command is `powershell -ExecutionPolicy Bypass -File scripts/build.ps1`; it builds Release and runs all six test executables plus renderer/inventory variants. No live GitHub App, production remote push or actual GitHub PR submission was used.

Build/test processes in the managed sandbox sometimes exit with code 1 without compiler diagnostics, and its process wrapping affected initial ConPTY startup. Scoped local build/test execution was used to obtain actual results. Diagnostic tests save only local reviewed bundles; they do not transmit reports.

## Package evidence

Target: self-contained `win-x64`, application version 0.1.0, unsigned portable directory `artifacts/GitCompass-win-x64` and ZIP `artifacts/GitCompass-0.1.0-win-x64.zip`. The full directory, local terminal assets, learning manifest and notices must travel together. Git for Windows and WebView2 remain prerequisites; no SDK or editor is required to run the published executable.

The produced ZIP was extracted and checked with `scripts/verify-package.ps1`. `.artifacts/package-verification-final.log` records a passing checksum and per-file SHA256/size inventory, no unlisted files, Windows x64 PE header and bundled-framework declaration. The actual extracted executable passed **two startup scenarios**, first-run and missing Git, with development runtime variables removed; each rendered offscreen and exited cleanly. Included files retain the local learning/terminal assets, dependency inventory and full component license/notice files, including the unchanged official Windows SDK license.

The actual extracted portable executable then passed **14 page/theme render scenarios** in `.artifacts/package-render-final.log`: all seven pages in light/dark, with worktree/index/refs/objects/config content fingerprints unchanged in Read-only. Captures are under `.artifacts/desktop-tests/e0c49f719bcf4cdc9224fdd051bede18`. The current Windows user token is not elevated (`ElevatedAdministratorToken=False`). These are development-machine runtime results, not a fresh-user/machine installation or supported-OS matrix.

Documentation is recopied before final ZIP integrity validation. Its exact final hash is recorded outside the ZIP in `artifacts/GitCompass-0.1.0-win-x64.zip.sha256` and the external `docs/package-report.md`; it is deliberately not embedded in this document. Fresh standard-user installation/update, missing-WebView2 behavior and company-supported OS acceptance remain separate gates.

## Milestone state

| Gate | State | Evidence still needed |
| --- | --- | --- |
| M0 Technical proof | Native technical proof recorded; manual/platform gate partial | Actual WPF, source executable startup/missing-Git shutdown, two-session ConPTY and local renderer proved. Native folder dialog and supported-machine matrix/manual accessibility still required. |
| M1 Read-only workspace | Automated core/native/portable evidence recorded; full gate partial | Real status/history, source and fourteen portable page fingerprint checks pass; recent dedup and permission default proved. Remaining discovery/manual switching/restart variants in AT01–AT09/AT20. |
| M2 Advice and observation | Automated advice/access and native interaction evidence recorded; full gate partial | Mixed-state/stale-preview/revocation proof, native output groups/copy controls, branch comparisons and page filters pass. Human comprehension, assistive technology and native timing/recovery variants remain. |
| M3 Guided local work | Real native stage/commit and Git effects proved; full gate partial | Actual index/commit/init/identity/ignore effects and permission controls pass. First-time human migration and remaining edge variants remain. |
| M4 Daily synchronization | Complete real two-clone suites recorded; full timing/native gate partial | 100 integration and 27 focused assertions pass, including fetch/verified push/fast-forward/divergence/conflict/server race/stash restoration conflict. Restored connectivity/authentication/quiet notification timing and full native sync workflow remain. |
| M5 Complete pilot | Open | Full P0 scenario coverage, accessibility/human tasks and fresh package acceptance; no complete pilot claim. |
| M6 Collaboration/reference | Offline reference, native comparison/filter and controlled hosted proofs recorded; external gate open | Catalog/inventory, comparison/filter and bounded hosted pagination checks pass. Teaching review, accessible graph navigation and live registered App sandbox remain. |
| M7 Release readiness | Open | Beginner pilot, measured representative performance, company OS matrix, fresh install/update, signing/distribution and complete release review. |

The [requirements checklist](requirements.md) and [acceptance scenario matrix](acceptance.md) map individual implementation and evidence. The [UI/UX review](ui-ux-review.md) compares all four supplied concepts and actual native screens. The [pilot task sheet](pilot-checklist.md) gives specific human/deployment tasks and comprehension outcomes. The final source and produced-package checks pass; the named scenario variants and human/external gates remain separate.

## External release gates

Live GitHub App registration/installation and a designated sandbox have not been supplied. The registered app needs device flow, Pull requests write and Contents read for this adapter; [research.md](research.md) links the official endpoint contracts. Git transport credentials are separate. Company Windows edition/build/architecture inventory, canonical branch policy, publisher copyright/end-user distribution terms, signing/distribution identity and representative project sizes remain external inputs. [Third-party notices](../THIRD_PARTY_NOTICES.md) retain the actual component license sources.

Manual keyboard/screen reader/high contrast/text scaling/DPI and real beginner task observation remain unexecuted. Windows 10/ARM64 and production install/update acceptance are unverified. These gates are retained explicitly; they are not inferred from compilation, palette provisions, an SDK target or screenshots.

## Performance observations

The measured fixture has **5,000 tracked files and 10,000 commits** on this development machine. The initial `.artifacts/performance-final.log` recorded first open **8,932 ms**, refreshes **2,891 / 2,677 / 2,193 ms**, and first 200 history rows **145 / 128 / 126 ms**. Status initially missed the proposed 2,000 ms target.

After bounded parallel Git metadata queries and file reads, `.artifacts/core-performance-final.log` records first open **1,790 ms**, refreshes **1,483 / 1,383 / 1,397 ms**, and first 200 history rows **176 / 129 / 120 ms**. Every recorded read is below 2,000 ms. Fingerprinting still hashes every selected byte and aggregates ordered results; it does not substitute modification-time caching for content evidence.

These are sequential development-machine observations on the same fixture, with no controlled cold disk cache, supported-company-machine, large-binary or general performance guarantee. UI output latency against the proposed 250 ms target has not been measured.
