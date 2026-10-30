# Verified local development environment

This records the local installation checked on 12 October 2026. Installation metadata and command-line discovery establish tool availability; they do not establish that every Visual Studio designer/debugger workflow or a fresh-machine application installation has passed acceptance.

## Installed tools

| Tool | Observed installation |
| --- | --- |
| Visual Studio | **Visual Studio Professional 2026, 18.10.3**, installation version `18.10.12224.181`; `C:\Program Files\Microsoft Visual Studio\18\Professional`. Installer discovery reports complete, launchable, stable and no reboot required. |
| WPF workload | `Microsoft.VisualStudio.Workload.ManagedDesktop` (**.NET desktop development**) is installed, confirmed by `vswhere -requires`. |
| System .NET SDK | **10.0.401**, `C:\Program Files\dotnet\sdk`. Matches the exact `global.json` pin, which disables roll-forward. |
| System runtimes | .NET and Windows Desktop/WPF **10.0.12**, plus **9.0.20**; ASP.NET Core **10.0.12**, under `C:\Program Files\dotnet\shared`. |
| Git | `C:\Program Files\Git\cmd\git.exe`; tested version **2.54.0.windows.1**. |
| VS Code Insiders | `C:\Users\<you>\AppData\Local\Programs\Microsoft VS Code Insiders`. Suitable for source editing and the command-line build scripts. |
| Workspace SDK | `.tools\dotnet\dotnet.exe`, SDK **10.0.401**, verified separately from the system installation. The build script prefers it when present. |

The Visual Studio setup registry records shared components at `F:\Dev\VSMicrosoft Visual Studio\Shared` and its package cache at `F:\Dev\VSMicrosoft\VisualStudio\Packages`. The literal folder `F:\Dev\VS` does **not** exist. These are the observed paths; do not infer the IDE installation location from the intended cache/shared-folder name. The IDE itself is installed on `C:` at the path above.

## Open and build

Open `F:\Dev\Projects\GitCompass\GitCompass.sln` in Visual Studio, choose `GitCompass.Desktop` as the startup project and use the Release configuration for the recorded build. The installed workload and SDK meet the documented [WPF setup](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/get-started/create-app-visual-studio); the [SDK/Visual Studio compatibility table](https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs) explains version coupling. GitCompass uses WPF and a Windows-targeted framework; opening the project in an editor does not install its runtime prerequisites.

From a PowerShell terminal in the repository, run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -ExecutionPolicy Bypass -File scripts/run.ps1
```

The build restores locked packages, builds Release and runs the executable checks. See [verification.md](verification.md) for actual results and remaining acceptance. `scripts/run.ps1` prefers an existing published portable app; if one exists, it runs that package rather than rebuilding edited source. Build/publish deliberately when verifying a change, or launch the intended freshly built output from Visual Studio.

VS Code Insiders can open the same folder and run these commands. Microsoft's [C# documentation](https://code.visualstudio.com/docs/languages/csharp) describes C# Dev Kit support. Its availability in the editor has not been separately recorded here; Visual Studio provides the WPF designer workflow. No persistent environment override is necessary for the installed system SDK to match `global.json`.

## Environment scope

The inspected **User** and **Machine** environment values for `DOTNET_ROOT`, `DOTNET_ROOT_X64`, `DOTNET_CLI_HOME`, `NUGET_PACKAGES`, `VSINSTALLDIR`, `MSBuildSDKsPath`, `DOTNET_CLI_TELEMETRY_OPTOUT` and `DOTNET_NOLOGO` are unset. The system `PATH` already contains `C:\Program Files\dotnet\` and `C:\Program Files\Git\cmd`. GitCompass development work has not written persistent User/Machine environment values, registry settings or `PATH` changes.

The repository scripts use ordinary process environment assignments. Run through the separate PowerShell process shown above, their values apply to that process and its children and disappear when it exits:

| Variable | `build.ps1` | `run.ps1` source fallback |
| --- | --- | --- |
| `DOTNET_ROOT` | `.tools\dotnet` when that SDK exists | `.tools\dotnet` |
| `DOTNET_CLI_HOME` | `.tools\cli-home` | `.tools\cli-home` |
| `NUGET_PACKAGES` | `.tools\nuget` | No assignment |
| `APPDATA` | `.tools\roaming` | `.tools\roaming` |
| `DOTNET_CLI_TELEMETRY_OPTOUT` | `1` | No assignment |
| `DOTNET_NOLOGO` | `1` | No assignment |

If `run.ps1` finds the portable executable, it starts it before making these development assignments. The scripts do not set `VSINSTALLDIR`, `MSBuildSDKsPath` or `DOTNET_ROOT_X64`. A developer shell may supply its own process values independently. Dot-sourcing a script would retain its process assignments in that shell; use the documented command invocation for the recorded scope.

## Application settings are separate

The SDK and NuGet directories above are development tool caches. GitCompass application settings, recent projects, reviewed/redacted logs and user-bound GitHub credentials use the Windows user's **LocalApplicationData\GitCompass** directory by default (`C:\Users\<you>\AppData\Local\GitCompass` for this user). The development `APPDATA` assignment changes a roaming environment variable; it does not redefine the application's LocalApplicationData store or isolate the entire user profile. Test hosts can explicitly supply an isolated application-state directory for fixtures.

The Git executable setting, GitHub App client ID, theme and monitoring preferences belong to GitCompass Settings. Access and repository trust are separate from IDE/environment configuration; **Allow changes** is a session permission and is not saved as a permanent unrestricted grant. Neither choosing an IDE nor building this solution grants the app permission to edit a selected project.

The self-contained portable package includes .NET and needs no SDK, Visual Studio, VS Code or Node.js to run. Installed Git and WebView2 remain prerequisites. Its instructions and human acceptance tasks are in [user-guide.md](user-guide.md) and [pilot-checklist.md](pilot-checklist.md).
