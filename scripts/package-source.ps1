$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$artifacts = Join-Path $workspace 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$appVersion = ([xml][IO.File]::ReadAllText((Join-Path $workspace 'Directory.Build.props'))).Project.PropertyGroup.Version
$zipPath = Join-Path $artifacts ('GitCompass-' + $appVersion + '-source.zip')
$rootFiles = @('.gitignore', 'README.md', 'THIRD_PARTY_NOTICES.md', 'global.json', 'NuGet.Config', 'GitCompass.sln', 'Directory.Build.props', 'Directory.Packages.props')
$rootFiles += @(('archive/output/pdf/GitCompass-Functional-Documentation-' + $appVersion + '.pdf'), ('archive/output/pdf/GitCompass-Functional-Documentation-' + $appVersion + '.html'))
$files = @($rootFiles | ForEach-Object { Get-Item -LiteralPath (Join-Path $workspace $_) })
foreach ($folder in @('src', 'tests', 'docs', 'archive', 'assets', 'scripts')) {
    $files += @(Get-ChildItem -LiteralPath (Join-Path $workspace $folder) -File -Recurse -Force | Where-Object {
        $relative = $_.FullName.Substring($workspace.Length + 1)
        $relative -notmatch '(^|[\\/])(bin|obj|node_modules|__pycache__|\.git|\.tools|\.artifacts|artifacts)([\\/]|$)'
    })
}
$files = @($files | Sort-Object FullName -Unique)
$records = @($files | ForEach-Object {
    [ordered]@{ Path = $_.FullName.Substring($workspace.Length + 1).Replace('\', '/'); Bytes = $_.Length; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::Create, [IO.FileAccess]::Write)
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    for ($i = 0; $i -lt $files.Count; $i++) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $files[$i].FullName, $records[$i].Path, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    $entry = $archive.CreateEntry('source-manifest.json')
    $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
    try { $writer.Write(([ordered]@{ App = 'GitCompass'; Version = $appVersion; Files = $records } | ConvertTo-Json -Depth 6)) } finally { $writer.Dispose() }
} finally { $archive.Dispose(); $stream.Dispose() }
$check = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($check.Entries.Count -ne $records.Count + 1) { throw 'Source archive entry count mismatch.' }
    foreach ($record in $records) {
        $entry = $check.GetEntry($record.Path)
        if (-not $entry -or $entry.Length -ne $record.Bytes) { throw "Source archive entry mismatch: $($record.Path)" }
        $input = $entry.Open()
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($hasher.ComputeHash($input)).Replace('-', '') } finally { $hasher.Dispose(); $input.Dispose() }
        if ($hash -ne $record.SHA256) { throw "Source archive hash mismatch: $($record.Path)" }
    }
} finally { $check.Dispose() }
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
[IO.File]::WriteAllText($zipPath + '.sha256', ($zipHash + '  ' + [IO.Path]::GetFileName($zipPath) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
Write-Output "Packaged source: $zipPath ($($records.Count) verified files)."
