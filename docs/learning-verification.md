# Offline learning and installed-command reconciliation

Observed on 12 October 2026 using installed Git for Windows **2.54.0.windows.1**, .NET SDK 10.0.401/runtime 10.0.12, Windows x64 build 26300. This is evidence for the observed installation, not a promise that every Git version or command option is supported.

The original offline manifest declares **133 core command reference entries** and includes **138 lessons in total**, including five workflow lessons. Every declared command has exactly one `git-<command>` entry. Every lesson contains purpose, prerequisites, local/remote effects, pause advice, a teaching example, an outcome and a disposable practice instruction. Command-reference coverage is broader than guided execution; a lesson does not unlock write permission or imply that advanced execution is implemented.

## Live installed inventory

The `GitCommandInventory.ReadAsync` adapter runs only these fixed read-only offline queries, through an absolute detected Git executable:

```text
git --no-pager -c color.ui=false -c help.autoCorrect=0 --version
git --no-pager -c color.ui=false -c help.autoCorrect=0 help -a --no-external-commands --no-aliases
```

It uses no shell, clears inherited Git overrides and secret application variables, disables global/system configuration and paging, closes stdin, independently drains bounded stdout/stderr and imposes a 15-second deadline. It never executes an enumerated command, alias, viewer or network operation. A failed/unknown inventory returns an error rather than an apparently successful zero count. Unrecognized sections/records or output truncation prevent a completeness claim.

| Reconciled fact | Observed count |
| --- | ---: |
| Installed executable-command entries listed by Git help | 153 |
| Declared command entries present in that inventory | 128 |
| Installed entries outside GitCompass's declared catalog | 25 |
| Declared entries not listed by this installed help inventory | 5 |
| Documentation interface topics excluded from executable commands | 19 |

Installed entries outside the declared catalog, which the Learn UI should explicitly label as unsupported by this catalog: `archimport`, `citool`, `cvsexportcommit`, `cvsimport`, `cvsserver`, `diagnose`, `diff-pairs`, `gitk`, `gitweb`, `gui`, `history`, `instaweb`, `last-modified`, `merge-one-file`, `p4`, `pack-redundant`, `quiltimport`, `repo`, `scalar`, `send-email`, `sh-i18n`, `sh-setup`, `survey`, `svn`, `whatchanged`.

Declared entries not listed by this installed help inventory: `http-fetch`, `http-push`, `receive-pack`, `upload-archive`, `upload-pack`. Absence from `git help -a` alone does **not** prove that an executable or manual is unavailable; the UI must retain that distinction. Documentation topics such as `attributes`, `hooks`, `ignore`, `revisions` and protocol/file-format topics are kept in a separate collection, rather than counted as executable commands.

## Executed checks

```powershell
$env:DOTNET_ROOT = (Resolve-Path .tools/dotnet).Path
& .tools/dotnet/dotnet.exe build tests/GitCompass.Terminal.Tests/GitCompass.Terminal.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false
& .tools/dotnet/dotnet.exe tests/GitCompass.Terminal.Tests/bin/Debug/net10.0-windows10.0.17763.0/GitCompass.Terminal.Tests.dll --inventory
```

Build succeeded with **0 warnings and 0 errors**. The inventory executable returned **exit 0, seven PASS records**: unique declarations/lesson IDs, full declared-entry coverage, required lesson fields, real offline enumeration, separate documentation topics, unsupported installed labels, and a fixture proving explicit unsupported/missing entries with section preservation. This validates catalog structure and enumeration, not every teaching command option or a beginner usability pilot. The UI must surface the adapter error and exact version/counts; it must not say that all installed commands are covered.

## Independent acceptance review

The updated engine was inspected after fixes: project-root redirection now invalidates a candidate registration; terminal and managed-operation leases authorize atomically by shared Git metadata identity; actual push URLs use `git remote get-url --push --all`; execution rechecks remote freshness/destination and reviewed index; untracked file diffs read a bounded prefix; independent stdout/stderr callbacks serialize shared state and isolate output-sink exceptions. These are static review observations. The separate core fixture suite supplies effect assertions.

The real terminal suites were rerun against the updated access registry: **24 native PASS records** and **five WebView2 renderer PASS records**, both exit 0. They cover real two-session I/O, resize, interruption, bounded output, revocation/owned job shutdown, immediate/failing startup, atomic shared-metadata shell/managed-operation exclusion, native single-line paste, actual xterm rendering and inert output/origin restrictions. See `tests/GitCompass.Terminal.Tests/README.md` for commands and platform evidence. Human multiline confirmation, selection/copy usability, keyboard/accessibility/DPI, Windows 10 and ARM64 remain separate validation gates.

No destructive `reset --hard`, `clean -f` or forced-push teaching example was found in the current catalog. Advanced `-h` examples are help-only; their effect fields describe the mutating variants. Review suggestions sent to the catalog owner: align journey/conflict `diff` examples with `--no-ext-diff --no-textconv`, and merge/switch examples with the engine's ignored-file preservation options. Teaching placeholders and disposable-practice guidance must remain visible; examples are never run automatically by Learn.
