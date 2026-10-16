param(
    [switch]$SkipBuild,
    [string]$VegasInstallPath = $env:VEGAS_INSTALL_PATH,
    [string]$InstallRoot = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Vegas Application Extensions')
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $PSScriptRoot 'build-extension.ps1'
$distDll = Join-Path $projectRoot 'dist\UnitedTaps.VegasMcp.dll'
$backupRoot = Join-Path $projectRoot 'backups'
# Fail closed if process enumeration itself fails.
$vegasProcesses = @(Get-CimInstance Win32_Process -ErrorAction Stop |
    Where-Object { $_.Name -match '^vegas.*\.exe$' })
if ($vegasProcesses.Count -gt 0) {
    $details = ($vegasProcesses | ForEach-Object { "PID $($_.ProcessId): $($_.Name)" }) -join ', '
    throw "Close every VEGAS process before installing the extension. Active process(es): $details"
}
if (-not $SkipBuild) { & $buildScript -VegasInstallPath $VegasInstallPath | Out-Host }
elseif (-not (Test-Path -LiteralPath $distDll)) { throw "The prebuilt VEGAS extension was not found: $distDll" }
$installedDll = Join-Path $InstallRoot 'UnitedTaps.VegasMcp.dll'
if (Test-Path -LiteralPath $installedDll) {
    $backup = Join-Path $backupRoot ("installed-extension-before-" + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    Copy-Item -LiteralPath $installedDll -Destination $backup
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
Copy-Item -LiteralPath $distDll -Destination $installedDll -Force
[pscustomobject]@{
    InstallRoot = $InstallRoot
    ExtensionDll = $installedDll
    TokenPath = $tokenPath
    VegasRestartRequired = $true
}
