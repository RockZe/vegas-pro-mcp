param([string]$VegasInstallPath = $env:VEGAS_INSTALL_PATH, [switch]$RequireLive)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'vegas-installation.ps1')
$target = Resolve-VegasInstallation -VegasInstallPath $VegasInstallPath
$root = $env:VEGAS_MCP_INSTALL_ROOT
if (-not $root) { $root = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Vegas Application Extensions' }
$dll = Join-Path $root 'UnitedTaps.VegasMcp.dll'
$token = Join-Path $root 'UnitedTaps.VegasMcp.token'
if ($env:VEGAS_MCP_TOKEN_FILE) { $token = $env:VEGAS_MCP_TOKEN_FILE }
$manifest = Join-Path $root 'UnitedTaps.VegasMcp.build.json'
$valid = $false
if ((Test-Path -LiteralPath $dll) -and (Test-Path -LiteralPath $manifest)) {
    $build = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    $valid = $build.target_file_version -eq $target.FileVersion -and
        $build.dll_sha256 -eq (Get-FileHash -LiteralPath $dll).Hash -and
        $build.api_sha256 -eq (Get-FileHash -LiteralPath $target.ApiPath).Hash
}
[pscustomobject]@{
    target = $target
    install_root = $root
    extension_exists = (Test-Path -LiteralPath $dll)
    build_manifest_matches = $valid
    token_exists = (Test-Path -LiteralPath $token)
    log_path = (Join-Path $root 'UnitedTaps.VegasMcp.runtime\vegas-extension.log')
    running_vegas = @(Get-Process -ErrorAction Stop | Where-Object { $_.ProcessName -match '^vegas' } | Select-Object Id,ProcessName)
} | ConvertTo-Json -Depth 5
if (-not $valid -or -not (Test-Path -LiteralPath $token)) { throw 'Connector installation is incomplete or does not match Build 31. Run install-extension.ps1.' }
if ($RequireLive) {
    & node (Join-Path $PSScriptRoot 'probe.mjs')
    if ($LASTEXITCODE -ne 0) { throw 'Live MCP probe failed. See the extension log and ensure Build 31 is running under the same Windows account.' }
}
