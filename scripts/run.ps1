param([string]$Project, [switch]$Development, [switch]$LiquidGlassPreview)
$workspace = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $workspace
if ($LiquidGlassPreview) { $Development = $true }
$launchArguments = @()
if ($Project) { $launchArguments += @('--project', $Project) }
if ($LiquidGlassPreview) { $launchArguments += @('--page', 'Liquid Glass preview') }
$appVersion = ([xml][IO.File]::ReadAllText((Join-Path $workspace 'Directory.Build.props'))).Project.PropertyGroup.Version
$releaseApp = Join-Path $workspace ('artifacts\GitCompass-' + $appVersion + '-win-x64\GitCompass.exe')
if (-not (Test-Path -LiteralPath $releaseApp) -and $appVersion -eq '0.1.0') { $releaseApp = Join-Path $workspace 'artifacts\GitCompass-win-x64\GitCompass.exe' }
if (-not $Development -and (Test-Path -LiteralPath $releaseApp)) {
    & $releaseApp @launchArguments
    exit $LASTEXITCODE
}
$env:DOTNET_ROOT = Join-Path $workspace '.tools\dotnet'
$env:DOTNET_CLI_HOME = Join-Path $workspace '.tools\cli-home'
$env:APPDATA = Join-Path $workspace '.tools\roaming'
$sdkCommand = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
if (-not (Test-Path -LiteralPath $sdkCommand)) { $sdkCommand = 'dotnet' }
& $sdkCommand run --project src\GitCompass.Desktop --no-restore -- @launchArguments
