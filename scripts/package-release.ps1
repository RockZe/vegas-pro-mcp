param([switch]$WithoutDependencies)
$ErrorActionPreference = 'Stop'
Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop
$root = Split-Path -Parent $PSScriptRoot
$package = Get-Content -LiteralPath (Join-Path $root 'package.json') -Raw | ConvertFrom-Json
$releaseRoot = Join-Path $root 'release'
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
$name = 'vegas-pro-mcp-' + $package.version
$staging = Join-Path $releaseRoot ($name + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $staging -Force | Out-Null
# Explicit allowlist: never package runtime state, .git, Codex config, tokens,
# test projects, private reports, source media or project/installation backups.
foreach ($item in @('src','extension','scripts','tests','docs','README.md','README.ru.md',
    'LICENSE','NOTICE.md','CHANGELOG.md','SECURITY.md','CONTRIBUTING.md',
    'package.json','package-lock.json','run-vegas-mcp.cmd')) {
    Copy-Item -LiteralPath (Join-Path $root $item) -Destination $staging -Recurse
}
$dist = Join-Path $staging 'dist'
New-Item -ItemType Directory -Path $dist | Out-Null
foreach ($item in @('UnitedTaps.VegasMcp.dll','build-manifest.json')) {
    Copy-Item -LiteralPath (Join-Path $root ('dist\' + $item)) -Destination $dist
}
if (-not $WithoutDependencies) {
    Copy-Item -LiteralPath (Join-Path $root 'node_modules') -Destination $staging -Recurse
}
$forbidden = @(Get-ChildItem -LiteralPath $staging -File -Recurse -Force | Where-Object {
    $_.Name -like '*.token' -or $_.Name -like '*.veg' -or $_.Name -eq 'ScriptPortal.Vegas.dll' -or $_.Name -like 'config.toml*'
})
if ($forbidden.Count -gt 0) { throw 'Forbidden runtime, private or proprietary files were found in the release staging directory.' }
$archive = Join-Path $releaseRoot ($name + '.zip')
Compress-Archive -LiteralPath $staging -DestinationPath $archive -CompressionLevel Optimal -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
[IO.File]::WriteAllText(($archive + '.sha256'), $hash + '  ' + [IO.Path]::GetFileName($archive) + "`r`n", [Text.Encoding]::ASCII)
Get-Item -LiteralPath $archive | Select-Object FullName,Length
Write-Output "SHA256 $hash"
