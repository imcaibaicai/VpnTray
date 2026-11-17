# Fetches the SOCKS5 core (pipeline.exe) from the upstream project's releases.
# The core is AGPL-3.0 and is NOT redistributed with this GUI.
# Usage:  powershell -NoProfile -ExecutionPolicy Bypass -File fetch-kernel.ps1
param(
    [string]$Dest = $PSScriptRoot,
    [string]$Repo = 'Yan233th/SHIEP-Pipeline'
)

$ErrorActionPreference = 'Stop'
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 | Out-Null } catch { }

Write-Host "querying $Repo releases..."
$headers = @{ 'User-Agent' = 'vpn-tray-fetch'; 'Accept' = 'application/vnd.github+json' }
$rel = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers $headers -UseBasicParsing
if (-not $rel) { throw "cannot read release info for $Repo" }

$asset = $rel.assets | Where-Object { $_.name -match 'windows' -and $_.name -match 'x64' -and $_.name -match '\.exe$' } | Select-Object -First 1
if (-not $asset) {
    $names = ($rel.assets | ForEach-Object { $_.name }) -join ', '
    throw "no windows-x64 exe in tag $($rel.tag_name). assets: $names"
}

$out = Join-Path $Dest 'pipeline.exe'
Write-Host "downloading $($asset.name) ($($asset.size) bytes, tag $($rel.tag_name)) ..."
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $out -UseBasicParsing -Headers @{ 'User-Agent' = 'vpn-tray-fetch' }

$len = (Get-Item $out).Length
Write-Host "saved: $out ($len bytes)"
& $out --version
