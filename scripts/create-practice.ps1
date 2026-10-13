$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$practice = Join-Path $workspace 'artifacts\GitCompass-Practice'
if (Test-Path -LiteralPath $practice) { throw 'The practice folder already exists. Keep your work; this script will not replace it.' }
$git = Join-Path $env:ProgramFiles 'Git\cmd\git.exe'
if (-not (Test-Path -LiteralPath $git)) { $git = (Get-Command git.exe -ErrorAction Stop).Source }
New-Item -ItemType Directory -Path $practice -Force | Out-Null
function Invoke-PracticeGit([string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($git)
    $start.WorkingDirectory = $practice
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($key in @($start.EnvironmentVariables.Keys)) { if ($key -like 'GIT_*') { $start.EnvironmentVariables.Remove($key) } }
    $start.EnvironmentVariables['GIT_CONFIG_GLOBAL'] = 'NUL'
    $start.EnvironmentVariables['GIT_CONFIG_SYSTEM'] = 'NUL'
    # Arguments are fixed script literals with no quotes; compatible with Windows PowerShell 5.1.
    $start.Arguments = ((@('-c', 'core.hooksPath=NUL', '-c', 'core.fsmonitor=false') + $Arguments | ForEach-Object { '"' + $_ + '"' }) -join ' ')
    $process = [Diagnostics.Process]::Start($start)
    try {
        $output = $process.StandardOutput.ReadToEndAsync(); $errorText = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit(); $output.Wait(); $errorText.Wait()
        if ($process.ExitCode -ne 0) { throw $errorText.Result }
        $output.Result
    } finally { $process.Dispose() }
}
Invoke-PracticeGit -Arguments @('init', '--initial-branch=main') | Out-Null
Invoke-PracticeGit -Arguments @('config', '--local', 'user.name', 'GitCompass Practice') | Out-Null
Invoke-PracticeGit -Arguments @('config', '--local', 'user.email', 'practice@example.invalid') | Out-Null
$encoding = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $practice 'welcome.txt'), "This is a disposable GitCompass practice project.`nThe first version is saved locally.`n", $encoding)
[IO.File]::WriteAllText((Join-Path $practice '.gitignore'), "scratch/`n", $encoding)
$instructions = @'
GitCompass practice project

This folder is disposable and has no remote or online connection configured.
Its practice author identity applies only to this folder.

1. Open Learn in GitCompass and read the local-to-uploaded workflow.
2. Select this folder and keep Access set to Read-only.
3. On Changes, inspect welcome.txt (modified) and ideas.txt (new).
4. On History graph, see the one locally saved version.
5. For now, just inspect. You do not need Monitor only, Allow changes or a terminal.

A commit saves selected contents locally. A push is a separate upload; this
practice project has no upload destination. You may remove this entire practice
folder later without affecting another project.
'@
[IO.File]::WriteAllText((Join-Path $practice 'READ-ME-FIRST.txt'), $instructions, $encoding)
Invoke-PracticeGit -Arguments @('add', '--', '.gitignore', 'welcome.txt', 'READ-ME-FIRST.txt') | Out-Null
Invoke-PracticeGit -Arguments @('commit', '-m', 'Save the first local practice version') | Out-Null
[IO.File]::AppendAllText((Join-Path $practice 'welcome.txt'), "This extra line is an edit after the saved version.`n", $encoding)
[IO.File]::WriteAllText((Join-Path $practice 'ideas.txt'), "This new file has not been saved in a Git version yet.`n", $encoding)
Invoke-PracticeGit -Arguments @('status', '--short')
$remotes = @(Invoke-PracticeGit -Arguments @('remote'))
if ($remotes.Where({ $_ }).Count -ne 0) { throw 'The practice project must have no configured remotes.' }
Write-Output "Created disposable Read-only practice project: $practice"
