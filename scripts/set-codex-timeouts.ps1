param([string]$Name = 'vegas-pro-2027', [string]$ConfigPath)
$ErrorActionPreference = 'Stop'
if ($Name -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Invalid MCP server name.' }
if ($ConfigPath) { $file = [IO.Path]::GetFullPath($ConfigPath) }
else {
    $codexRoot = $env:CODEX_HOME
    if (-not $codexRoot) { $codexRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex' }
    $file = Join-Path $codexRoot 'config.toml'
}
$codexRoot = Split-Path -Parent $file
$config = [IO.File]::ReadAllText($file)
$pattern = '(?m)^\[mcp_servers\.' + [Regex]::Escape($Name) + '\]\r?$'
$heading = [Regex]::Match($config, $pattern)
if (-not $heading.Success) { throw "No registered MCP table for $Name was found." }
$next = [Regex]::Match($config.Substring($heading.Index + $heading.Length), '(?m)^\[')
$end = if ($next.Success) { $heading.Index + $heading.Length + $next.Index } else { $config.Length }
$section = $config.Substring($heading.Index, $end - $heading.Index)
$updated = $section
foreach ($entry in @(@('startup_timeout_sec','30'), @('tool_timeout_sec','210'))) {
    $key = $entry[0]; $value = $entry[1]
    if ($updated -match ('(?m)^' + $key + '\s*=')) {
        $updated = [Regex]::Replace($updated, ('(?m)^' + $key + '[ \t]*=[^\r\n]*'), ($key + ' = ' + $value))
    } else { $updated = $updated.TrimEnd() + "`r`n$key = $value`r`n`r`n" }
}
if ($updated -ne $section) {
    # Preserve the complete existing config locally; never package this backup.
    $backup = Join-Path $codexRoot ('config.before-vegas-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8) + '.toml.bak')
    Copy-Item -LiteralPath $file -Destination $backup
    [IO.File]::WriteAllText($file, $config.Substring(0, $heading.Index) + $updated + $config.Substring($end), [Text.UTF8Encoding]::new($false))
}
Write-Host "Codex '$Name': startup timeout 30 s; tool timeout 210 s (connector deadline 180 s)."
