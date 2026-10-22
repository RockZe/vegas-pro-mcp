# Shared by build, install, launch and diagnostics. Never select by the folder name alone.
Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop
function Get-VegasTarget {
    param([Parameter(Mandatory=$true)][string]$Path)
    $directory = [IO.Path]::GetFullPath($Path).TrimEnd('\','/')
    $exe = Join-Path $directory 'vegas2027.exe'
    $api = Join-Path $directory 'ScriptPortal.Vegas.dll'
    foreach ($file in @($exe, $api)) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Required VEGAS 2027 file was not found: $file" }
        $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($file)
        $actual = '{0}.{1}.{2}.{3}' -f $version.FileMajorPart,$version.FileMinorPart,$version.FileBuildPart,$version.FilePrivatePart
        if ($actual -ne '2027.0.0.31') { throw "Refusing incompatible VEGAS file: $file ($actual). Required: 2027.0.0.31." }
    }
    [pscustomobject]@{ InstallPath=$directory; ExecutablePath=$exe; ApiPath=$api; FileVersion='2027.0.0.31' }
}

function Resolve-VegasInstallation {
    param([string]$VegasInstallPath = $env:VEGAS_INSTALL_PATH)
    if ($VegasInstallPath) { return Get-VegasTarget -Path $VegasInstallPath }
    $candidates = @()
    foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if (-not $base) { continue }
        foreach ($brand in @('BorisFX','VEGAS','MAGIX')) {
            $candidates += Join-Path $base ($brand + '\Vegas Pro 2027')
        }
    }
    foreach ($key in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')) {
        Get-ChildItem -LiteralPath $key -ErrorAction SilentlyContinue | ForEach-Object {
            $entry = Get-ItemProperty -LiteralPath $_.PSPath -ErrorAction SilentlyContinue
            if ($entry.DisplayName -match '(?i)vegas' -and $entry.InstallLocation) { $candidates += $entry.InstallLocation }
        }
    }
    $matches = @($candidates | Select-Object -Unique | ForEach-Object {
        try { Get-VegasTarget -Path $_ } catch { } # Other installed VEGAS versions are not eligible.
    } | Sort-Object InstallPath -Unique)
    if ($matches.Count -eq 0) { throw 'VEGAS Pro 2027.0.0 Build 31 was not found. Pass -VegasInstallPath or set VEGAS_INSTALL_PATH. No other version will be used.' }
    if ($matches.Count -gt 1) { throw ('Multiple Build 31 installations found. Pass -VegasInstallPath explicitly: ' + ($matches.InstallPath -join ', ')) }
    return $matches[0]
}

function Assert-VegasClosed {
    # Get-Process works without WMI/CIM access and fails closed on enumeration errors.
    $running = @(Get-Process -ErrorAction Stop | Where-Object { $_.ProcessName -match '^vegas.*$' })
    if ($running.Count -gt 0) {
        throw ('Close every VEGAS process before installing the extension. Active process(es): ' +
            (($running | ForEach-Object { 'PID ' + $_.Id + ': ' + $_.ProcessName }) -join ', '))
    }
}
