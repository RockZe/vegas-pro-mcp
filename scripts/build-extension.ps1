param(
    [string]$VegasInstallPath = $env:VEGAS_INSTALL_PATH
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $projectRoot 'dist'
$sourceRoot = Join-Path $projectRoot 'extension'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw "C# compiler was not found: $csc. Enable .NET Framework 4.8 on Windows." }
if (-not $VegasInstallPath) {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'BorisFX\Vegas Pro 2026'),
        (Join-Path $env:ProgramFiles 'VEGAS\VEGAS Pro 2026'),
        (Join-Path $env:ProgramFiles 'MAGIX\VEGAS Pro 2026')
    )
    $VegasInstallPath = $candidates | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'ScriptPortal.Vegas.dll') } | Select-Object -First 1
}
if (-not $VegasInstallPath) { throw 'VEGAS Pro 2026 was not found. Pass -VegasInstallPath with its installation directory.' }
$vegasApi = Join-Path $VegasInstallPath 'ScriptPortal.Vegas.dll'
if (-not (Test-Path -LiteralPath $vegasApi)) { throw "VEGAS scripting API was not found: $vegasApi" }
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
$output = Join-Path $distRoot 'UnitedTaps.VegasMcp.dll'
$sources = Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $csc /nologo /target:library /optimize+ /warn:4 /out:$output `
    /reference:$vegasApi /reference:System.dll /reference:System.Core.dll `
    /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    /reference:System.Web.Extensions.dll /reference:System.Security.dll $sources
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $output)) { throw 'The VEGAS MCP application extension did not compile.' }
Get-Item -LiteralPath $output | Select-Object FullName, Length, LastWriteTime
