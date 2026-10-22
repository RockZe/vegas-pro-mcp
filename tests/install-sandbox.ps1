$ErrorActionPreference = 'Stop'
# Load utility functions before the process-list fixture shadows Get-Process.
Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop
$projectRoot = Split-Path -Parent $PSScriptRoot
$sandbox = Join-Path $projectRoot ('work\installer-' + [Guid]::NewGuid().ToString('N'))
$sandbox = [IO.Path]::GetFullPath($sandbox)
if (-not $sandbox.StartsWith([IO.Path]::GetFullPath((Join-Path $projectRoot 'work')) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected installer sandbox path.' }
$scripts = Join-Path $sandbox 'scripts'
$dist = Join-Path $sandbox 'dist'
$install = Join-Path $sandbox 'installed extension'
New-Item -ItemType Directory -Path $scripts,$dist -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts\install-extension.ps1') -Destination $scripts
Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts\vegas-installation.ps1') -Destination $scripts
$fakeVegas = Join-Path $sandbox 'fake VEGAS 2027'
New-Item -ItemType Directory -Path $fakeVegas -Force | Out-Null
$fixtureSource = Join-Path $sandbox 'version.cs'
[IO.File]::WriteAllText($fixtureSource, '[assembly:System.Reflection.AssemblyFileVersion("2027.0.0.31")] public class Program { public static void Main() {} }')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fixtureExe = Join-Path $fakeVegas 'vegas2027.exe'
& $compiler /nologo /target:exe /out:$fixtureExe $fixtureSource
if ($LASTEXITCODE -ne 0) { throw 'Fixture compiler failed.' }
Copy-Item -LiteralPath (Join-Path $fakeVegas 'vegas2027.exe') -Destination (Join-Path $fakeVegas 'ScriptPortal.Vegas.dll')
$dll = Join-Path $dist 'UnitedTaps.VegasMcp.dll'
# The production installer is run against disposable placeholder bytes only.
# No VEGAS installation or work project is read or written by this harness.
[IO.File]::WriteAllText($dll, 'fixture-dll-version-one')
function Get-Process { @() }
function Write-FixtureManifest {
    @{ target_file_version='2027.0.0.31'; api_sha256=(Get-FileHash -LiteralPath (Join-Path $fakeVegas 'ScriptPortal.Vegas.dll')).Hash;
        dll_sha256=(Get-FileHash -LiteralPath $dll).Hash } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dist 'build-manifest.json') -Encoding UTF8
}
Write-FixtureManifest
$installer = Join-Path $scripts 'install-extension.ps1'
$first = & $installer -SkipBuild -VegasInstallPath $fakeVegas -InstallRoot $install
if ($first.ExtensionDll -ne (Join-Path $install 'UnitedTaps.VegasMcp.dll')) { throw 'Wrong destination.' }
$tokenPath = Join-Path $install 'UnitedTaps.VegasMcp.token'
$firstToken = [IO.File]::ReadAllText($tokenPath)
if ($firstToken.Trim().Length -lt 32) { throw 'Invalid generated token.' }
if (-not [IO.File]::GetAccessControl($tokenPath).AreAccessRulesProtected) { throw 'Token permissions still inherit.' }
if ([IO.File]::ReadAllText($first.ExtensionDll) -ne 'fixture-dll-version-one') { throw 'Wrong DLL contents.' }
[IO.File]::WriteAllText($dll, 'fixture-dll-version-two')
Write-FixtureManifest
$second = & $installer -SkipBuild -VegasInstallPath $fakeVegas -InstallRoot $install
if ([IO.File]::ReadAllText($tokenPath) -ne $firstToken) { throw 'Reinstallation replaced the token.' }
if ([IO.File]::ReadAllText($second.ExtensionDll) -ne 'fixture-dll-version-two') { throw 'DLL update failed.' }
$backups = @(Get-ChildItem -LiteralPath (Join-Path $sandbox 'backups') -Filter '*.dll' -Recurse)
if ($backups.Count -ne 1 -or [IO.File]::ReadAllText($backups[0].FullName) -ne 'fixture-dll-version-one') { throw 'Rollback DLL was not preserved.' }
Write-Host 'PASS isolated installer: first install, protected token, retained token, DLL update, rollback backup.'
[IO.File]::WriteAllText($dll, 'tampered fixture')
$rejected = $false
try { & $installer -SkipBuild -VegasInstallPath $fakeVegas -InstallRoot $install } catch {
    if ($_.Exception.Message -notmatch 'Build manifest does not match') { throw }
    $rejected = $true
}
if (-not $rejected -or [IO.File]::ReadAllText($second.ExtensionDll) -ne 'fixture-dll-version-two') { throw 'Tampered DLL was not rejected before replacement.' }
function Get-Process { [pscustomobject]@{Id=123;ProcessName='vegas230'} }
$rejected = $false
try { & $installer -SkipBuild -VegasInstallPath $fakeVegas -InstallRoot $install } catch {
    if ($_.Exception.Message -notmatch 'Close every VEGAS process') { throw }
    $rejected = $true
}
if (-not $rejected) { throw 'Active older VEGAS did not block installation.' }
. (Join-Path $scripts 'vegas-installation.ps1')
$olderVegas = Join-Path $sandbox 'older VEGAS'
New-Item -ItemType Directory -Path $olderVegas -Force | Out-Null
[IO.File]::WriteAllText($fixtureSource, '[assembly:System.Reflection.AssemblyFileVersion("2027.0.0.32")] public class Program { public static void Main() {} }')
$fixtureExe = Join-Path $olderVegas 'vegas2027.exe'
& $compiler /nologo /target:exe /out:$fixtureExe $fixtureSource
if ($LASTEXITCODE -ne 0) { throw 'Fixture compiler failed.' }
Copy-Item -LiteralPath (Join-Path $olderVegas 'vegas2027.exe') -Destination (Join-Path $olderVegas 'ScriptPortal.Vegas.dll')
$rejected = $false
try { Resolve-VegasInstallation -VegasInstallPath $olderVegas } catch {
    if ($_.Exception.Message -notmatch 'Refusing incompatible') { throw }
    $rejected = $true
}
if (-not $rejected) { throw 'Wrong build was accepted.' }
Write-Host 'PASS exact-version rejection, tampered-manifest refusal and active older VEGAS refusal.'
