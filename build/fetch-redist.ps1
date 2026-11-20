# Downloads the official, Authenticode-signed driver installers that reController embeds
# (ViGEmBus = BSD-3-Clause, HidHide = MIT, both by Nefarius Software Solutions e.U.).
# Every file is pinned by SHA-256 and its signature is checked. Run automatically by the build when missing.
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File build/fetch-redist.ps1 [-Destination redist]
param(
    [string]$Destination = (Join-Path (Split-Path -Parent $PSScriptRoot) 'redist')
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$packages = @(
    @{
        Name   = 'ViGEmBus.exe'
        Url    = 'https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe'
        Sha256 = '89220a7865076b342892f98865f3499fb7c4cfd673159e89d352c360fd014c6a'
    },
    @{
        Name   = 'HidHide.exe'
        Url    = 'https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe'
        Sha256 = 'f4bbbcb82e6258641b887c74bc81c4c5f66e4aa811808dfc304347687b7605f6'
    }
)

New-Item -ItemType Directory -Force $Destination | Out-Null

foreach ($p in $packages) {
    $target = Join-Path $Destination $p.Name
    if (Test-Path $target) {
        if ((Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant() -eq $p.Sha256) {
            Write-Host "[redist] $($p.Name) OK (cached)"
            continue
        }
        Remove-Item $target -Force
    }

    $tmp = "$target.download"
    Write-Host "[redist] downloading $($p.Url)"
    Invoke-WebRequest -Uri $p.Url -OutFile $tmp -UseBasicParsing

    $hash = (Get-FileHash $tmp -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $p.Sha256) {
        Remove-Item $tmp -Force
        throw "[redist] SHA-256 mismatch for $($p.Name): expected $($p.Sha256), got $hash"
    }

    $sig = Get-AuthenticodeSignature $tmp
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Nefarius Software Solutions') {
        Remove-Item $tmp -Force
        throw "[redist] invalid signature on $($p.Name): $($sig.Status) $($sig.SignerCertificate.Subject)"
    }

    Move-Item $tmp $target -Force
    Write-Host "[redist] $($p.Name) verified"
}
