$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sandbox = Join-Path $projectRoot ('work\installer-' + [Guid]::NewGuid().ToString('N'))
$sandbox = [IO.Path]::GetFullPath($sandbox)
if (-not $sandbox.StartsWith([IO.Path]::GetFullPath((Join-Path $projectRoot 'work')) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected installer sandbox path.' }
$scripts = Join-Path $sandbox 'scripts'
$dist = Join-Path $sandbox 'dist'
$install = Join-Path $sandbox 'installed extension'
New-Item -ItemType Directory -Path $scripts,$dist -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts\install-extension.ps1') -Destination $scripts
$dll = Join-Path $dist 'UnitedTaps.VegasMcp.dll'
# The production installer is run against disposable placeholder bytes only.
# No VEGAS installation or work project is read or written by this harness.
[IO.File]::WriteAllText($dll, 'fixture-dll-version-one')
function Get-CimInstance { @() }
$installer = Join-Path $scripts 'install-extension.ps1'
$first = & $installer -SkipBuild -InstallRoot $install
if ($first.ExtensionDll -ne (Join-Path $install 'UnitedTaps.VegasMcp.dll')) { throw 'Wrong destination.' }
$tokenPath = Join-Path $install 'UnitedTaps.VegasMcp.token'
$firstToken = [IO.File]::ReadAllText($tokenPath)
if ($firstToken.Trim().Length -lt 32) { throw 'Invalid generated token.' }
if (-not [IO.File]::GetAccessControl($tokenPath).AreAccessRulesProtected) { throw 'Token permissions still inherit.' }
if ([IO.File]::ReadAllText($first.ExtensionDll) -ne 'fixture-dll-version-one') { throw 'Wrong DLL contents.' }
[IO.File]::WriteAllText($dll, 'fixture-dll-version-two')
$second = & $installer -SkipBuild -InstallRoot $install
if ([IO.File]::ReadAllText($tokenPath) -ne $firstToken) { throw 'Reinstallation replaced the token.' }
if ([IO.File]::ReadAllText($second.ExtensionDll) -ne 'fixture-dll-version-two') { throw 'DLL update failed.' }
$backups = @(Get-ChildItem -LiteralPath (Join-Path $sandbox 'backups') -Filter '*.dll' -Recurse)
if ($backups.Count -ne 1 -or [IO.File]::ReadAllText($backups[0].FullName) -ne 'fixture-dll-version-one') { throw 'Rollback DLL was not preserved.' }
Write-Host 'PASS isolated installer: first install, protected token, retained token, DLL update, rollback backup.'
