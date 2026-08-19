param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$portable = Join-Path $repoRoot 'scripts\Publish-Portable.ps1'
$isccCandidates = @(@(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path -LiteralPath $_ })
if ($isccCandidates.Count -eq 0) { throw '未检测到 Inno Setup 6。请先安装：winget install --id JRSoftware.InnoSetup --exact' }

& $portable -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Portable 发布失败。' }
& $isccCandidates[0] (Join-Path $repoRoot 'installer\CodexCleaner.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup 打包失败。' }

$installer = Join-Path $repoRoot 'artifacts\installer\CodexCleaner-0.0.1-Setup.exe'
$portableArchive = Join-Path $repoRoot 'artifacts\portable\CodexCleaner-win-x64.zip'
$checksum = Join-Path $repoRoot 'artifacts\CodexCleaner-v0.0.1-SHA256.txt'
Get-FileHash -Algorithm SHA256 -LiteralPath $installer | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $($_.Path | Split-Path -Leaf)" } | Set-Content -LiteralPath ($installer + '.sha256') -Encoding utf8
@(
    (Get-FileHash -Algorithm SHA256 -LiteralPath $installer | ForEach-Object { "$($_.Hash.ToLowerInvariant())  CodexCleaner-0.0.1-Setup.exe" }),
    (Get-FileHash -Algorithm SHA256 -LiteralPath $portableArchive | ForEach-Object { "$($_.Hash.ToLowerInvariant())  CodexCleaner-win-x64.zip" })
) | Set-Content -LiteralPath $checksum -Encoding utf8
Write-Output $installer
