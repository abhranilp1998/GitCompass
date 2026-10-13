param([switch]$SkipRestore, [switch]$SkipTests, [switch]$Publish, [switch]$MeasurePerformance)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $workspace
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_HOME = Join-Path $workspace '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $workspace '.tools\nuget'
$env:APPDATA = Join-Path $workspace '.tools\roaming'
$bundledSdk = Join-Path $workspace '.tools\dotnet\dotnet.exe'
$sdkCommand = if (Test-Path -LiteralPath $bundledSdk) { $env:DOTNET_ROOT = Split-Path $bundledSdk -Parent; $bundledSdk } else { 'dotnet' }
function Invoke-Sdk([string[]]$Arguments) {
    & $sdkCommand @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE" }
}
if (-not $SkipRestore) { Invoke-Sdk -Arguments @('restore', 'GitCompass.sln', '--configfile', 'NuGet.Config', '--locked-mode') }
Invoke-Sdk -Arguments @('build', 'GitCompass.sln', '-c', 'Release', '--no-restore', '-m:1', '-p:UseSharedCompilation=false')
if (-not $SkipTests) {
    $testProjects = @(
        'tests\GitCompass.Domain.Tests\GitCompass.Domain.Tests.csproj',
        'tests\GitCompass.Git.IntegrationTests\GitCompass.Git.IntegrationTests.csproj',
        'tests\GitCompass.Adapters.Tests\GitCompass.Adapters.Tests.csproj',
        'tests\GitCompass.Desktop.Tests\GitCompass.Desktop.Tests.csproj',
        'tests\GitCompass.Desktop.WorkflowTests\GitCompass.Desktop.WorkflowTests.csproj',
        'tests\GitCompass.Terminal.Tests\GitCompass.Terminal.Tests.csproj'
    )
    foreach ($testProject in $testProjects) {
        $testArguments = @('run', '--project', $testProject, '-c', 'Release', '--no-build', '--no-restore')
        if ($testProject -like '*GitCompass.Desktop.Tests*') {
            $desktopExe = Join-Path $workspace 'src\GitCompass.Desktop\bin\Release\net10.0-windows10.0.17763.0\GitCompass.exe'
            if (-not (Test-Path -LiteralPath $desktopExe)) { $desktopExe = Join-Path $workspace 'src\GitCompass.Desktop\bin\Release\net10.0-windows10.0.17763.0\win-x64\GitCompass.exe' }
            $testArguments += @('--', $desktopExe)
        }
        Invoke-Sdk -Arguments $testArguments
        if ($testProject -like '*GitCompass.Desktop.Tests*') {
            Invoke-Sdk -Arguments @('run', '--project', $testProject, '-c', 'Release', '--no-build', '--no-restore', '--', $desktopExe, '--startup-only')
        }
    }
    Invoke-Sdk -Arguments @('run', '--project', 'tests\GitCompass.Git.IntegrationTests\GitCompass.Git.IntegrationTests.csproj', '-c', 'Release', '--no-build', '--no-restore', '--', '--focused')
    Invoke-Sdk -Arguments @('run', '--project', 'tests\GitCompass.Terminal.Tests\GitCompass.Terminal.Tests.csproj', '-c', 'Release', '--no-build', '--no-restore', '--', '--webview')
    Invoke-Sdk -Arguments @('run', '--project', 'tests\GitCompass.Terminal.Tests\GitCompass.Terminal.Tests.csproj', '-c', 'Release', '--no-build', '--no-restore', '--', '--inventory')
}
if ($MeasurePerformance) {
    Invoke-Sdk -Arguments @('run', '--project', 'tests\GitCompass.Performance.Tests\GitCompass.Performance.Tests.csproj', '-c', 'Release', '--no-build', '--no-restore')
}
if ($Publish) {
    $publishStaging = Join-Path $workspace ('.artifacts\publish-' + [guid]::NewGuid().ToString('N'))
    Invoke-Sdk -Arguments @('publish', 'src\GitCompass.Desktop\GitCompass.Desktop.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore', '-p:PublishSingleFile=false', '-m:1', '-p:UseSharedCompilation=false', '-o', $publishStaging)
    & (Join-Path $PSScriptRoot 'package.ps1') -PublishedDirectory $publishStaging
}

