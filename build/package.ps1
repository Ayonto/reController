# Builds a release: one self-contained reController.exe with the drivers embedded, packaged as
#   dist/reController-<version>-portable.zip   (unzip & run; data stays next to the exe)
#   dist/reController-<version>-win-x64.exe    (same app, stores data in %AppData%)
#   dist/SHA256SUMS.txt
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File build/package.ps1 [-Version 1.2.3] [-SkipTests]
param(
    [string]$Version,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $Version) {
    $Version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.VersionPrefix | Where-Object { $_ } | Select-Object -First 1
}
Write-Host "== reController $Version"

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'fetch-redist.ps1')
if ($LASTEXITCODE) { throw 'fetch-redist failed' }

if (-not $SkipTests) {
    dotnet test ReController.sln -c Release --nologo
    if ($LASTEXITCODE) { throw 'tests failed' }
}

$publish = Join-Path $root 'artifacts/publish/win-x64'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish src/ReController.App -p:PublishProfile=win-x64 "-p:Version=$Version" --nologo
if ($LASTEXITCODE) { throw 'publish failed' }

$exe = Join-Path $publish 'reController.exe'
$selftest = & $exe --selftest | Out-String
if ($selftest -notmatch '\[PASS\] Bundled driver installers') { throw "Published exe does not contain the drivers:`n$selftest" }

$dist = Join-Path $root 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null

# Portable package
$stage = Join-Path $root "artifacts/stage/reController"
if (Test-Path (Split-Path $stage)) { Remove-Item (Split-Path $stage) -Recurse -Force }
New-Item -ItemType Directory $stage -Force | Out-Null
Copy-Item $exe $stage
Copy-Item LICENSE (Join-Path $stage 'LICENSE.txt')
Copy-Item THIRD-PARTY-NOTICES.md $stage
Set-Content (Join-Path $stage 'portable.txt') 'This file makes reController portable: settings and profiles are stored in the "data" folder next to reController.exe. Delete it to store them in %AppData% instead.'
@"
reController $Version
====================

1. Double-click reController.exe.
2. The first time, click "Install" in the One-time setup window and approve the Windows prompt.
   (This installs the two bundled controller drivers, ViGEmBus and HidHide.)
3. Restart Windows if asked. That's it.

Fix a broken button: open the Live input page (3rd dot at the bottom) to find it, then on the Mapping
page click a spare button, choose Gamepad, pick the broken button, and press Apply.
"@ | Set-Content (Join-Path $stage 'README.txt')
Compress-Archive -Path $stage -DestinationPath (Join-Path $dist "reController-$Version-portable.zip")

Copy-Item $exe (Join-Path $dist "reController-$Version-win-x64.exe")

Get-ChildItem $dist -File | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name } |
    Set-Content (Join-Path $dist 'SHA256SUMS.txt')

Write-Host "== done"
Get-ChildItem $dist | Format-Table Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
