# GitCompass

A Windows desktop workspace for people learning Git. Open a project folder, see what changed, understand where work is saved, and review the next useful action.

The current source includes the [Windows 11 Fluent and beginner walkthrough update](docs/beginner-fluent-update.md): System/Light/Dark themes, searchable navigation, an optional resumable guide, and explicit first-version setup. Run `powershell -ExecutionPolicy Bypass -File scripts/run.ps1 -Development` to use this source build. The existing 0.1.1 portable ZIP and exported manuals describe the earlier build; they have not been repackaged for this update.

The [responsive layout and glass update](docs/responsive-glass-update.md) adds compact navigation, stacked controls and smaller activity/terminal toolbars down to 640 × 560. Settings → Window background offers Mica, Glass (Acrylic), and Solid independently of light/dark appearance. Native effects follow Windows transparency and accessibility preferences.

For a stronger glass experiment, open **Settings → Try a different design → Open Liquid Glass preview**, or run `powershell -ExecutionPolicy Bypass -File scripts/run.ps1 -LiquidGlassPreview`. This launches the current source directly into the [experimental preview](docs/liquid-glass-preview.md), with sampled blur, curved-edge refraction, glossy rims, local Light/Dark controls and sample data.

The [complete functional documentation](docs/functional-documentation.md) explains every screen and guided workflow, permissions, state changes, recovery, settings, storage, limitations and verified acceptance. It is the detailed manual; [the user guide](docs/user-guide.md) is the shorter starting path.

Reading copies are available as a [printable PDF](output/pdf/GitCompass-Functional-Documentation-0.1.1.pdf) and [self-contained offline HTML](output/pdf/GitCompass-Functional-Documentation-0.1.1.html). Regenerate them with `python scripts/export-functional-documentation.py`; the manual's maintenance section lists the export dependencies and checks. The [0.1.1 inspection update](docs/inspection-update-0.1.1.md) explains branch viewing in Read-only/Monitor only, plain-folder guidance and the separate updated trial.

GitCompass implements the supplied build pack as one native application. The original specification and four UI concepts are preserved under [`reference/`](reference/); [`docs/specification.md`](docs/specification.md) records their role and the GitCompass naming decision. Current evidence and remaining release gates are recorded in [`docs/verification.md`](docs/verification.md).

## Development tools

For the WPF designer and Windows debugging, install **Visual Studio 2026**, select **.NET desktop development**, and include the **.NET 10** component. Open `GitCompass.sln` and choose `GitCompass.Desktop` as the startup project. Microsoft's [WPF setup tutorial](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/get-started/create-app-visual-studio) documents this combination. Use a current Visual Studio update compatible with the pinned SDK; the [SDK/Visual Studio version table](https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs) records feature-band requirements.

**VS Code Insiders also works for editing and building this source** through the scripts below. C# support is provided by Microsoft's [C# Dev Kit](https://code.visualstudio.com/docs/languages/csharp). The scripts already prefer the verified workspace-local SDK, so installing an IDE is not blocking implementation. An IDE's SDK discovery is separate from that script setting: if Visual Studio cannot resolve `global.json`, install the exact 10.0.401 SDK system-wide or configure its SDK environment deliberately. Do not remove the version pin to hide a setup error.

This machine now has **Visual Studio Professional 2026 18.10.3**, the .NET desktop workload and system **.NET SDK 10.0.401** installed. The [verified development environment](docs/development-environment.md) records the actual paths, process-only script settings and their separation from application settings.

## Build and run

Use Windows x64, the pinned .NET 10.0.401 SDK, Git for Windows (tested version recorded in verification), and the WebView2 Evergreen runtime for terminals. The initial platform is Windows 11; compatibility with particular Windows 10 editions/builds and ARM64 has to be tested separately.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -ExecutionPolicy Bypass -File scripts/run.ps1
# Open a folder for inspection only:
powershell -ExecutionPolicy Bypass -File scripts/run.ps1 -Project 'C:\Projects\YourProject'
# Create the self-contained x64 internal package:
powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -Publish
```

Development restoration needs NuGet network access. The optional workspace-local SDK under `.tools/dotnet` is used when present. It is excluded from source control and is not an application prerequisite. Terminal JS/CSS is vendored under `assets/terminal/vendor`; users do not need Node.js or a CDN. To deliberately refresh those pinned assets as a developer, run `npm ci` then `npm run vendor` inside `assets/terminal` and review the resulting license/source changes.

The current internal package is a portable directory and ZIP, produced under `artifacts/GitCompass-0.1.1-win-x64` and `artifacts/GitCompass-0.1.1-win-x64.zip`. The original 0.1.0 copy remains separate. Extract the **whole ZIP** and run `GitCompass.exe` beside its files. It bundles .NET; users do not need Visual Studio, VS Code or a .NET SDK. Git and WebView2 remain detected prerequisites. The package is unsigned and is not yet a production release. See the [user guide](docs/user-guide.md) for setup and a first disposable-project trial.

Start with Learn, then open the prepared `artifacts/GitCompass-Practice` folder in Read-only. It contains one local commit, one modified file and one new file, with no remote. It is separate from the application ZIP. On another source checkout, `scripts/create-practice.ps1` creates the disposable example and refuses to replace an existing folder.

## Project structure

`Domain` holds immutable facts and deterministic advice. `Application` owns access, reviewed previews and coordinated execution. `Infrastructure.Git` handles typed CLI processes and parsing; `Infrastructure.GitHub` handles hosted requests and device authorization; `Infrastructure.Local` stores settings, redacted logs and user-bound credentials. `Terminal.Windows` owns ConPTY and its restricted WebView bridge. `Desktop` provides the shared WPF shell. Test executables assert real behavior without requiring an external test service.

New projects open in Read-only. Monitoring permits narrowly scoped fetch and hosted reads. Allow changes unlocks reviewed writes and terminal sessions for the current app session. Every effect is checked by the engine or terminal gate. The application permission mode does not restrict other editors or Git tools.

GitHub API features need a registered GitHub App public client ID with device flow enabled and the correct installation permissions. Git transport credentials are separate. The [research and setup record](docs/research.md) identifies official documentation, tested versions and the exact hosted permissions. Live GitHub sandbox, company OS inventory, signing/distribution decisions, and a representative beginner pilot remain external release gates.

## Verification records

[`docs/requirements.md`](docs/requirements.md) maps FR01–FR20 to implementation, proof and gaps. [`docs/acceptance.md`](docs/acceptance.md) maps all AT01–AT37 and AT08a scenarios. [`docs/learning-verification.md`](docs/learning-verification.md) records the declared offline command catalog and installed inventory; [`docs/ui-ux-review.md`](docs/ui-ux-review.md) records the concept and actual-window review. Automated fixtures use disposable local repositories and controlled HTTP responses, without live GitHub submissions.

[`docs/task-handoff.md`](docs/task-handoff.md) reconciles the original and resumed subagent assignments with their deliverables and executed evidence, including the interrupted-session recovery.
