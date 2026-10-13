# Terminal verification

Executable smoke tests use real Windows ConPTY and PowerShell, not mocked console streams. All commands run in one dedicated temporary test folder. Only that folder and its owned job processes are cleaned up.

Run after restoring/building:

```powershell
$env:DOTNET_ROOT = (Resolve-Path .tools/dotnet).Path
& .tools/dotnet/dotnet.exe tests/GitCompass.Terminal.Tests/bin/Debug/net10.0-windows10.0.17763.0/GitCompass.Terminal.Tests.dll
& .tools/dotnet/dotnet.exe tests/GitCompass.Terminal.Tests/bin/Debug/net10.0-windows10.0.17763.0/GitCompass.Terminal.Tests.dll --webview
```

For a restricted build executor, set DOTNET_CLI_HOME and APPDATA to workspace-local `.tools/cli-home` and `.tools/roaming`. Restore uses the repository NuGet.config, and compilation can use `-m:1 -p:UseSharedCompilation=false` to avoid inaccessible cross-session compiler pipes.

Observed 12 October 2026: Windows 11 build 26300 x64, .NET SDK 10.0.401/runtime 10.0.12, WebView2 Runtime 154.0.4258.62. Both smoke runs passed. The terminal project compiled without C# warnings; restricted NuGet vulnerability metadata lookup produced NU1900 until network-enabled restore.

Latest build command: `.tools/dotnet/dotnet.exe build tests/GitCompass.Terminal.Tests/GitCompass.Terminal.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false`. Result: **Build succeeded, 0 warnings, 0 errors**. Native harness: exit 0, 24 PASS records, approximately 2.8 seconds on the observed machine. Renderer harness `--webview`: exit 0, 5 PASS records, approximately 3.8 seconds. These are integration-smoke durations, not app performance benchmarks.

Native coverage: new-project Read-only and Monitor only shell denial; two real isolated PowerShell sessions; API token environment exclusion; 120×40 resize observed by shell; bounded 600 KB output with visible truncation; Ctrl+C interrupts a 60-second sleep and shell remains usable; pending downgrade blocks input immediately; transition completes only after terminal lease/job closes; owned descendant process terminates; natural shell exit/disposal; foreign-origin and malformed/duplicate/extra-field bridge rejection.

Lifecycle regression coverage additionally creates/closes five sessions immediately during shell startup, then exercises invalid executable startup. Every lease releases, independently drained final ConPTY output closes cleanly, and no shutdown timeout occurs.

Shared metadata regression coverage registers two project IDs under the same case-insensitive common identity: an active managed lease rejects shell creation in the other project, and an active real shell rejects a managed lease in the other project. Closing the shell releases its group ownership.

Renderer coverage: creates WPF WebView2 with bundled offline xterm.js; verifies actual xterm buffer contains real shell output; native single-line paste plus Enter; output with script-looking text remains text; OSC clipboard/link output creates no browser links; external navigation blocked.

The renderer uses a hidden test window to exercise initialization; this is integration evidence, not a visual/accessibility audit. Multiline paste has a native review dialog showing the full text with cancel/default-safe behavior; its human confirmation, interactive selection/copy, keyboard focus, high contrast and DPI behavior still need a desktop usability check. OS claims apply only to the observed machine; Windows 10 and ARM64 remain untested.

The same executable's `--inventory` mode audits the offline learning manifest and runs the read-only installed Git inventory adapter. It currently reports seven passes; full observed counts and unsupported/missing-inventory distinctions are recorded in `docs/learning-verification.md`.
