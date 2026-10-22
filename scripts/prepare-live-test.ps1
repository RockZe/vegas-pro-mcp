param([string]$VegasInstallPath = $env:VEGAS_INSTALL_PATH)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'vegas-installation.ps1')
Assert-VegasClosed
$target = Resolve-VegasInstallation -VegasInstallPath $VegasInstallPath
$projectRoot = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $projectRoot ('work\live-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$script = Join-Path $fixture 'bootstrap.cs'
Copy-Item -LiteralPath (Join-Path $projectRoot 'tests\native\bootstrap.cs') -Destination $script
# /SCRIPT is documented by VEGAS; bootstrap refuses any existing work project.
$process = Start-Process -FilePath $target.ExecutablePath -ArgumentList @('/NOLOGO','/SCRIPT',('"' + $script + '"')) -WorkingDirectory $target.InstallPath -PassThru
[IO.File]::WriteAllText((Join-Path $projectRoot 'work\latest-live-fixture.txt'), $fixture)
$deadline = [DateTime]::UtcNow.AddSeconds(45)
while (-not (Test-Path -LiteralPath (Join-Path $fixture 'bootstrap.ok'))) {
    if (Test-Path -LiteralPath (Join-Path $fixture 'bootstrap.error.txt')) { throw ([IO.File]::ReadAllText((Join-Path $fixture 'bootstrap.error.txt'))) }
    if ($process.HasExited -or [DateTime]::UtcNow -ge $deadline) { throw "VEGAS bootstrap did not complete. Inspect $fixture and the editor window." }
    Start-Sleep -Milliseconds 250
}
[pscustomobject]@{ Fixture=$fixture; ProcessId=$process.Id; Project=(Join-Path $fixture 'acceptance.veg') }
