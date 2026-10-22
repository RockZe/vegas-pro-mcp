param(
    [switch]$SkipBuild,
    [string]$VegasInstallPath = $env:VEGAS_INSTALL_PATH,
    [string]$InstallRoot = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Vegas Application Extensions')
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'vegas-installation.ps1')
$target = Resolve-VegasInstallation -VegasInstallPath $VegasInstallPath
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $PSScriptRoot 'build-extension.ps1'
$distDll = Join-Path $projectRoot 'dist\UnitedTaps.VegasMcp.dll'
$backupRoot = Join-Path $projectRoot 'backups'
Assert-VegasClosed
if (-not $SkipBuild) { & $buildScript -VegasInstallPath $target.InstallPath | Out-Host }
elseif (-not (Test-Path -LiteralPath $distDll)) { throw "The prebuilt VEGAS extension was not found: $distDll" }
$manifestPath = Join-Path $projectRoot 'dist\build-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.target_file_version -ne $target.FileVersion -or
    $manifest.api_sha256 -ne (Get-FileHash -LiteralPath $target.ApiPath -Algorithm SHA256).Hash -or
    $manifest.dll_sha256 -ne (Get-FileHash -LiteralPath $distDll -Algorithm SHA256).Hash) {
    throw 'Build manifest does not match the extension DLL and installed Build 31 API. Rebuild before installing.'
}
$installedDll = Join-Path $InstallRoot 'UnitedTaps.VegasMcp.dll'
if (Test-Path -LiteralPath $installedDll) {
    $backup = Join-Path $backupRoot ("installed-extension-before-" + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    Copy-Item -LiteralPath $installedDll -Destination $backup
    $oldManifest = Join-Path $InstallRoot 'UnitedTaps.VegasMcp.build.json'
    if (Test-Path -LiteralPath $oldManifest) { Copy-Item -LiteralPath $oldManifest -Destination $backup }
    Write-Host "Preserved installed extension at $backup"
}
New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
foreach ($directory in @('UnitedTaps.VegasMcp.instances','UnitedTaps.VegasMcp.runtime')) {
    New-Item -ItemType Directory -Path (Join-Path $InstallRoot $directory) -Force | Out-Null
}
$tokenPath = Join-Path $InstallRoot 'UnitedTaps.VegasMcp.token'
if (-not (Test-Path -LiteralPath $tokenPath)) {
    $bytes = New-Object byte[] 48
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    [IO.File]::WriteAllText($tokenPath, [Convert]::ToBase64String($bytes), [Text.UTF8Encoding]::new($false))
}
if (([IO.File]::ReadAllText($tokenPath).Trim()).Length -lt 32) { throw 'Existing connector token is invalid. Preserve it and investigate before installing.' }
# SID forms work on localized Windows installations and domain accounts.
$accountSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
& icacls.exe $tokenPath /inheritance:r /grant:r "*${accountSid}:M" '*S-1-5-18:F' '*S-1-5-32-544:F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not restrict the connector token permissions.' }
Assert-VegasClosed
Copy-Item -LiteralPath $distDll -Destination $installedDll -Force
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $InstallRoot 'UnitedTaps.VegasMcp.build.json') -Force
[pscustomobject]@{
    InstallRoot = $InstallRoot
    ExtensionDll = $installedDll
    TokenPath = $tokenPath
    VegasRestartRequired = $true
    VegasExecutable = $target.ExecutablePath
    TargetFileVersion = $target.FileVersion
}
