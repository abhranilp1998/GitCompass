# -ManualVersion selects which exported functional manual to bundle when the app version has no regenerated manual yet.
param([Parameter(Mandatory = $true)][string]$PublishedDirectory, [string]$ManualVersion)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$published = [IO.Path]::GetFullPath($PublishedDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $published 'GitCompass.exe'))) { throw 'Publish GitCompass before packaging.' }
$appVersion = ([Version][Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $published 'GitCompass.dll')).FileVersion).ToString(3)
if (-not $ManualVersion) { $ManualVersion = $appVersion }
function Copy-Required([string]$Source, [string]$Name) {
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { throw "Required package content is missing: $Source" }
    Copy-Item -LiteralPath $Source -Destination (Join-Path $published $Name)
}
Copy-Required (Join-Path $workspace 'THIRD_PARTY_NOTICES.md') 'THIRD_PARTY_NOTICES.md'
Copy-Required (Join-Path $workspace 'docs\user-guide.md') 'user-guide.md'
Copy-Required (Join-Path $workspace 'docs\pilot-checklist.md') 'pilot-checklist.md'
Copy-Required (Join-Path $workspace 'docs\verification.md') 'verification.md'
Copy-Required (Join-Path $workspace ('archive\output\pdf\GitCompass-Functional-Documentation-' + $appVersion + '.pdf')) 'functional-documentation.pdf'
Copy-Required (Join-Path $workspace ('archive\output\pdf\GitCompass-Functional-Documentation-' + $appVersion + '.html')) 'functional-documentation.html'
Copy-Required (Join-Path $workspace 'docs\licenses\WindowsSDK-license.rtf') 'WindowsSDK-LICENSE.rtf'
$documentation = Join-Path $published 'docs'
New-Item -ItemType Directory -Path $documentation -Force | Out-Null
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $workspace 'docs') -File -Recurse) {
    # The report contains this ZIP's final hash and is deliberately external to the ZIP.
    if ($file.Name -eq 'package-report.md') { continue }
    $relative = $file.FullName.Substring((Join-Path $workspace 'docs').Length + 1)
    $target = Join-Path $documentation $relative
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
}
[IO.File]::WriteAllText((Join-Path $published 'verification.md'), "# GitCompass verification`n`nSee [the complete verification record](docs/verification.md). The source archive includes the original build-pack references.`n", [Text.UTF8Encoding]::new($false))
$packageCache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $workspace '.tools\nuget' }
Copy-Required (Join-Path $packageCache 'microsoft.web.webview2\1.0.4258.31\LICENSE.txt') 'WebView2-SDK-LICENSE.txt'
Copy-Required (Join-Path $packageCache 'microsoft.web.webview2\1.0.4258.31\NOTICE.txt') 'WebView2-SDK-NOTICE.txt'
Copy-Required (Join-Path $packageCache 'communitytoolkit.mvvm\8.4.2\License.md') 'MVVM-Toolkit-LICENSE.md'
Copy-Required (Join-Path $packageCache 'communitytoolkit.mvvm\8.4.2\ThirdPartyNotices.txt') 'MVVM-Toolkit-NOTICES.txt'
Copy-Required (Join-Path $packageCache 'microsoft.netcore.app.runtime.win-x64\10.0.12\LICENSE.TXT') 'dotnet-runtime-LICENSE.txt'
Copy-Required (Join-Path $packageCache 'microsoft.netcore.app.runtime.win-x64\10.0.12\THIRD-PARTY-NOTICES.TXT') 'dotnet-runtime-NOTICES.txt'
Copy-Required (Join-Path $packageCache 'microsoft.windowsdesktop.app.runtime.win-x64\10.0.12\LICENSE') 'WPF-runtime-LICENSE.txt'
foreach ($item in @('coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll', 'assets\learning\manifest.json', 'terminal\vendor\xterm.js', 'terminal\vendor\xterm.LICENSE', 'terminal\vendor\addon-fit.LICENSE')) {
    if (-not (Test-Path -LiteralPath (Join-Path $published $item) -PathType Leaf)) { throw "Incomplete self-contained package: $item" }
}
$readme = @'
# GitCompass 0.1.0 - Windows x64 internal pilot

Extract the whole ZIP into a folder you can write to, then open GitCompass.exe.
Keep its DLLs, assets and terminal folders together. .NET is included; Visual Studio,
VS Code, Node.js and the development SDK are not needed to run this package.

Prerequisites: Windows x64, installed Git for Windows, and Microsoft WebView2
Evergreen Runtime for the Terminal tab. Settings and logs use your Windows user's
local application data, outside your project. The app does not silently install prerequisites.

Select a project folder. New projects open in Read-only. Review the displayed facts
before selecting Monitor only or Allow changes. Saving a commit is local; uploading
is a separate reviewed action. See user-guide.md for the beginner workflow.

This is an unsigned portable internal pilot. It has no system installer, auto-updater
or verified production signing identity. See verification.md and pilot-checklist.md
for exact tests and remaining human/live-service/OS validation gates.

GitHub API features need a registered GitHub App public client ID and permissions.
Git transport authentication is separate. Local Git, lessons and graph work offline.

To remove the app, close it fully and remove the extracted application folder.
Your project files and separately stored app settings are not removed automatically.
Do not delete project folders as part of removing GitCompass.
'@
[IO.File]::WriteAllText((Join-Path $published 'START-HERE.md'), $readme.Replace('GitCompass 0.1.0', 'GitCompass ' + $appVersion), [Text.UTF8Encoding]::new($false))
$dependencies = [ordered]@{
    App = 'GitCompass'; Version = $appVersion; RuntimeIdentifier = 'win-x64'; SelfContained = $true
    DotnetRuntime = '10.0.12'; WpfRuntime = '10.0.12'; WindowsSdkNet = '10.0.17763.57'; MVVMToolkit = '8.4.2'; WebView2Sdk = '1.0.4258.31'
    Xterm = '5.5.0'; XtermFitAddon = '0.10.0'; Git = 'separately installed'; WebView2Runtime = 'separately installed'
    Signing = 'unsigned internal pilot'; Telemetry = 'no automatic diagnostic transmission'
}
[IO.File]::WriteAllText((Join-Path $published 'dependencies.json'), ($dependencies | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
$files = @(Get-ChildItem -LiteralPath $published -File -Recurse -Force | Where-Object { $_.Name -ne 'package-manifest.json' } | Sort-Object FullName | ForEach-Object {
    [ordered]@{ Path = $_.FullName.Substring($published.TrimEnd('\').Length + 1).Replace('\', '/'); Bytes = $_.Length; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$manifest = [ordered]@{ App = 'GitCompass'; Version = $appVersion; BuiltUtc = [DateTimeOffset]::UtcNow.ToString('O'); Files = $files }
[IO.File]::WriteAllText((Join-Path $published 'package-manifest.json'), ($manifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
$artifacts = Join-Path $workspace 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$destination = [IO.Path]::GetFullPath((Join-Path $artifacts ('GitCompass-' + $appVersion + '-win-x64')))
if (Test-Path -LiteralPath $destination) {
    if (-not $destination.StartsWith($artifacts + '\', [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath (Join-Path $destination 'package-manifest.json'))) { throw 'Refusing to replace an unrecognized output folder.' }
    $previous = Join-Path $artifacts ('GitCompass-' + $appVersion + '-win-x64-previous-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
    Move-Item -LiteralPath $destination -Destination $previous
}
Copy-Item -LiteralPath $published -Destination $destination -Recurse
$zip = Join-Path $artifacts ('GitCompass-' + $appVersion + '-win-x64.zip')
Compress-Archive -Path (Join-Path $destination '*') -DestinationPath $zip -Force
[IO.File]::WriteAllText($zip + '.sha256', ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash + '  ' + [IO.Path]::GetFileName($zip) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
Write-Output "Packaged $zip ($($files.Count) manifest files)."
