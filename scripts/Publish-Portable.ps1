param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$appProject = Join-Path $repoRoot 'src\CodexCleaner.App\CodexCleaner.App.csproj'
$helperProject = Join-Path $repoRoot 'src\CodexCleaner.ElevatedHelper\CodexCleaner.ElevatedHelper.csproj'
$publishRoot = Join-Path $repoRoot 'artifacts\portable\CodexCleaner-win-x64'
$archive = Join-Path $repoRoot 'artifacts\portable\CodexCleaner-win-x64.zip'

dotnet publish $appProject -c $Configuration -r win-x64 -p:PublishProfile=Portable-win-x64 -p:DeleteExistingFiles=true
dotnet publish $helperProject -c $Configuration -r win-x64 --self-contained true -o (Join-Path $publishRoot 'ElevatedHelper')
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $archive -Force
Write-Output $archive
