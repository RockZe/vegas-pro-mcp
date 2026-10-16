@echo off
setlocal
where node.exe >nul 2>nul
if errorlevel 1 (
  echo Node.js 22 or newer is required. Install Node.js and restart your MCP client. 1>&2
  exit /b 1
)
node.exe "%~dp0src\server.mjs"
exit /b %errorlevel%
