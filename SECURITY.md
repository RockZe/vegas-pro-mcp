# Security

This is a local editing connector with the permissions of your Windows account.
Install only source you trust and review the changes your assistant requests.

The stdio server opens no HTTP listener. VEGAS accepts named-pipe connections
with a current-account ACL and checks a random local token. This is a boundary
against other accounts, not a sandbox against malicious code running as you.
Treat your MCP client and other programs under your account as trusted peers.

Keep the installed token, runtime files, logs and backups private. Do not share
these files in issues or paste a token into model prompts. Tool results include
local paths and project metadata; your AI client's data handling still applies.

For a suspected vulnerability, use this repository's **Security > Report a
vulnerability** link when available. If private reporting is unavailable, open
an issue requesting a private contact route without exploit details, credentials,
project names or customer information. Do not publish a working exploit in an issue.

Only the latest public beta is currently maintained. There is no promise of
production support or exhaustive compatibility across media and third-party effects.
