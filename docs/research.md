# Research and setup record

Checked against primary vendor documentation on 12 October 2026. Version observations below describe this development machine; they are not an expanded compatibility promise.

## Editor and SDK

Microsoft's [WPF tutorial](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/get-started/create-app-visual-studio) lists Visual Studio 2026, the .NET desktop development workload and the .NET 10 component. It includes the XAML designer. For this Windows WPF project, Visual Studio is the recommended companion IDE.

Microsoft also documents [C# editing/debugging in VS Code](https://code.visualstudio.com/docs/languages/csharp). Using VS Code Insiders with the SDK and GitCompass's build scripts is a workable source-editing and command-line build path. That recommendation is based on the actual CLI build here, rather than a claim of identical WPF designer features. [SDK and Visual Studio feature bands](https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs) must match; use a current Visual Studio update and the repository's pinned SDK 10.0.401.

## Portable deployment and prerequisites

The build publishes `win-x64` as self-contained and keeps assets beside the executable. A [self-contained .NET deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/) bundles its runtime and needs a new app publication to update that bundled runtime. It does not install Git or the WebView2 runtime. The maintainer must rebuild and reverify for runtime/security updates.

Install Git through the official [Git for Windows download page](https://git-scm.com/downloads/win). Tested here: 2.54.0.windows.1. The specification's proposed 2.43 minimum has not been validated as a complete compatibility matrix.

For the terminal, use Microsoft's [WebView2 Evergreen runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/). The [distribution guide](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution) distinguishes the online bootstrapper from the full Standalone Installer for offline setup. Local terminal JS/CSS is shipped; users do not need Node.js. Tested runtime: 154.0.4258.62.

Initial application validation is x64 on Windows build 26300. The [current .NET Windows installation/support guidance](https://learn.microsoft.com/en-us/dotnet/core/install/windows) lists specific supported Windows releases. Neither the `windows10.0.17763.0` target-framework API baseline nor one development-machine run proves support for every Windows 10 edition, Windows 11 build, or ARM64 machine. Company OS inventory and fresh-machine acceptance remain required.

## Git behavior behind the UI

[Git fetch](https://git-scm.com/docs/git-fetch) updates retrieved objects and mapped refs; it does not integrate them into the current branch. Its explicit `--refmap` contract permits overriding configured mappings. GitCompass scopes Monitor-only fetch to approved remote-tracking refs and disables tag/maintenance/submodule side effects; malicious local-branch/tag mappings are covered by disposable fixtures.

[Git push](https://git-scm.com/docs/git-push) supports explicit source/destination refspecs and normally rejects updates that would discard remote history. GitCompass reviews one resolved push URL and branch at a time, verifies the resulting destination, and offers no automatic forced push. Remote-check freshness and successful upload verification remain separate facts.

## GitHub App setup

The administrator must register a GitHub App, enable device flow and install it on the designated repositories. Configure its public **client ID**, not its App ID or a client secret. Users authorize in the system browser; Git transport credentials remain separate. [GitHub's user-token guide](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-user-access-token-for-a-github-app) documents device authorization and access limited by both the app and user permissions.

For this adapter, request **Pull requests: write** for deliberate PR creation and **Contents: read** for verifying the published source branch before submission. These are the permission sets documented for [Create a pull request](https://docs.github.com/en/rest/pulls/pulls?apiVersion=2026-03-10#create-a-pull-request) and [Get a reference](https://docs.github.com/en/rest/git/refs?apiVersion=2026-03-10#get-a-reference). Metadata read is part of normal app repository access. To display check runs on private repositories also grant **Checks: read**, as documented for [List check runs](https://docs.github.com/en/rest/checks/runs?apiVersion=2026-03-10#list-check-runs-for-a-git-reference). Restricted review/check information is shown as unavailable, not passing. Repository administration is not required for the local graph.

GitHub's [pull-request listing](https://docs.github.com/en/rest/pulls/pulls?apiVersion=2026-03-10#list-pull-requests) limits a page to 100 records. Its [pagination guide](https://docs.github.com/en/rest/using-the-rest-api/using-pagination-in-the-rest-api) documents continuation links. The adapter uses fixed same-origin page paths, retains each cached page's continuation for conditional responses, and stops with an explicit incomplete-result error if its bounded listing is exhausted. This avoids presenting the first page as all requests or blindly resubmitting after incomplete reconciliation.

[GitHub's refresh-token protocol](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/refreshing-user-access-tokens) makes the client secret optional when the token originated from device flow. GitCompass's controlled-response tests cover polling intervals, slow-down, expiry/rotation, revoked credentials, rate limits, inaccessible repositories, uncertain submission and matching-PR reconciliation. No live registration, account or sandbox has been supplied, so these tests do not establish real sign-in or hosted acceptance.

No API token belongs in `settings.json`, process arguments, logs, screenshots or a terminal environment. Current-user Windows protection stores GitHub credentials separately. The app does not collect a GitHub password.
