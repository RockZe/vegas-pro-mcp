param([string]$Name = 'vegas-pro-2027', [switch]$PrintOnly)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
if ($Name -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Use a simple MCP server name containing letters, numbers, underscores or hyphens.' }
$root = Split-Path -Parent $PSScriptRoot
$node = (Get-Command node.exe -ErrorAction Stop).Source
$server = Join-Path $root 'src\server.mjs'
$install = $env:VEGAS_MCP_INSTALL_ROOT
if (-not $install) { $install = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Vegas Application Extensions' }
if ($PrintOnly) {
    # Literal TOML strings preserve Windows backslashes; no connector token is exposed.
    if (($node + $server + $install) -match "'") { throw 'A path contains an apostrophe. Use the Codex CLI registration instead.' }
    Write-Output "[mcp_servers.$Name]"
    Write-Output "command = '$node'"
    Write-Output "args = ['$server']"
    Write-Output 'startup_timeout_sec = 30'
    Write-Output 'tool_timeout_sec = 210'
    Write-Output "[mcp_servers.$Name.env]"
    Write-Output "VEGAS_MCP_INSTALL_ROOT = '$install'"
    return
}
$codex = (Get-Command codex.exe -ErrorAction Stop).Source
$servers = & $codex mcp list --json
if ($LASTEXITCODE -ne 0) { throw 'Could not read Codex MCP configuration.' }
$decodedServers = ($servers -join "`n") | ConvertFrom-Json
$existing = $decodedServers | Where-Object { $_.name -eq $Name } | Select-Object -First 1
if ($existing) {
    if ($existing.transport.command -eq $node -and $existing.transport.args.Count -eq 1 -and
        $existing.transport.args[0] -eq $server -and $existing.transport.env.VEGAS_MCP_INSTALL_ROOT -eq $install) {
        Write-Host "Codex MCP server '$Name' is already configured for this checkout."
        & (Join-Path $PSScriptRoot 'set-codex-timeouts.ps1') -Name $Name
        return
    }
    throw "Codex already has a different MCP server named '$Name'. Choose -Name instead of overwriting it."
}
& $codex mcp add $Name --env "VEGAS_MCP_INSTALL_ROOT=$install" -- $node $server
if ($LASTEXITCODE -ne 0) { throw 'Codex MCP registration failed.' }
& (Join-Path $PSScriptRoot 'set-codex-timeouts.ps1') -Name $Name
Write-Host 'Reconnect MCP or restart Codex to load the newly registered tools.'
