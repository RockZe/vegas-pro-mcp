param(
    [string]$VegasInstallPath = $env:VEGAS_INSTALL_PATH
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'vegas-installation.ps1')
$target = Resolve-VegasInstallation -VegasInstallPath $VegasInstallPath
$projectRoot = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $projectRoot 'dist'
$sourceRoot = Join-Path $projectRoot 'extension'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw "C# compiler was not found: $csc. Enable .NET Framework 4.8 on Windows." }
$vegasApi = $target.ApiPath
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
$output = Join-Path $distRoot 'UnitedTaps.VegasMcp.dll'
$sources = Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $csc /nologo /target:library /platform:x64 /optimize+ /warn:4 /out:$output `
    /reference:$vegasApi /reference:System.dll /reference:System.Core.dll `
    /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    /reference:System.Web.Extensions.dll /reference:System.Security.dll $sources
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $output)) { throw 'The VEGAS MCP application extension did not compile.' }
$manifest = [ordered]@{
    target_file_version = $target.FileVersion
    vegas_executable = $target.ExecutablePath
    api_path = $vegasApi
    api_assembly = [Reflection.AssemblyName]::GetAssemblyName($vegasApi).FullName
    api_sha256 = (Get-FileHash -LiteralPath $vegasApi -Algorithm SHA256).Hash
    dll_sha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
    built_utc = [DateTime]::UtcNow.ToString('o')
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $distRoot 'build-manifest.json') -Encoding UTF8
Get-Item -LiteralPath $output | Select-Object FullName, Length, LastWriteTime
