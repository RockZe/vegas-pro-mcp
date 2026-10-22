param([string]$VegasInstallPath = $env:VEGAS_INSTALL_PATH, [string]$ProjectPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'vegas-installation.ps1')
$target = Resolve-VegasInstallation -VegasInstallPath $VegasInstallPath
# This launches the editor visibly. Never use file associations, which can select another VEGAS.
$arguments = @('/NOLOGO')
if ($ProjectPath) {
    $ProjectPath = [IO.Path]::GetFullPath($ProjectPath)
    if (-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf) -or [IO.Path]::GetExtension($ProjectPath) -ne '.veg') { throw 'ProjectPath must be an existing VEGAS project.' }
    $arguments += @('/OPEN', ('"' + $ProjectPath + '"'))
}
Start-Process -FilePath $target.ExecutablePath -ArgumentList $arguments -WorkingDirectory $target.InstallPath -PassThru |
    Select-Object Id,ProcessName
