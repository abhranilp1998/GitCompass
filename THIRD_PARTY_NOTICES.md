# Third-party notices and dependency inventory

GitCompass implementation dependencies are pinned in `global.json`, `Directory.Packages.props`, project `packages.lock.json` files, and `assets/terminal/package-lock.json`.

| Component | Version | License / source |
| --- | --- | --- |
| .NET SDK | 10.0.401 | Microsoft .NET; MIT-licensed open-source components and bundled third-party notices. Development only. |
| .NET runtime/WPF | 10.0.12 | Microsoft .NET; bundled runtime licenses and notices accompany self-contained publication. |
| CommunityToolkit.Mvvm | 8.4.2 | MIT; https://github.com/CommunityToolkit/dotnet |
| Microsoft.Web.WebView2 SDK | 1.0.4258.31 | Microsoft WebView2 SDK license; NuGet package includes LICENSE.txt. |
| Microsoft.Windows.SDK.NET.Ref | 10.0.17763.57 | Microsoft Windows SDK license; projection/runtime assemblies `Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll` are distributed unmodified. Original license: `docs/licenses/WindowsSDK-license.rtf` in source, `WindowsSDK-LICENSE.rtf` in the portable package. |
| WebView2 runtime | detected at run time | Separately installed Microsoft prerequisite and license. |
| @xterm/xterm | 5.5.0 | MIT; full license in `assets/terminal/vendor/xterm.LICENSE`. |
| @xterm/addon-fit | 0.10.0 | MIT; full license in `assets/terminal/vendor/addon-fit.LICENSE`. |
| Git for Windows | installed separately | GPLv2; https://gitforwindows.org/. GitCompass uses an installed CLI and does not redistribute Git. |

Transitive NuGet dependencies are retained in project lockfiles. Review installed prerequisites and all package/runtime license files before redistribution. The original learning content belongs to this implementation; Git manuals are linked rather than copied. The supplied AI-generated UI concepts are reference images, not shipped screenshots or a statement of completion.

The installed Windows SDK package metadata identifies [Microsoft's Windows SDK license](https://aka.ms/WinSDKLicenseURL). The original RTF was obtained from that official URL on 12 October 2026 and retained unchanged (SHA256 `DD07EB178E00C6BBA4148457FC00FF77CD4887EB521D504186FE59C9EC8BBE62`). Microsoft's [Windows SDK redistribution list](https://learn.microsoft.com/en-us/legal/windows-sdk/redist) explicitly lists these two .NET assemblies for unmodified distribution as part of a program that calls WinRT APIs, subject to the SDK terms. Retain Microsoft copyright/trademark notices and the supplied license. The distribution terms also require the application's valid copyright notice and protective terms for external end users/distributors; the publisher must resolve its ownership and external distribution agreement before a production release.

The portable package includes full license/notice files for .NET, WPF, MVVM Toolkit, WebView2 SDK, Windows SDK and xterm. Git and the WebView2 Runtime are separately installed and are not included. Production distribution, signing and the supported OS matrix remain explicit release gates.
