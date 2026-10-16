# Contributing

Small bug reports and focused pull requests are welcome. Include your Windows,
Node, and VEGAS version/build, the exact tool/action, and a sanitized example.

Run `npm ci --ignore-scripts`, `npm run check`, and `npm run smoke`. On Windows,
run `npm run test:native`. If you have VEGAS Pro 2026 installed, also build the
extension using `scripts/build-extension.ps1`.

Preserve the saved-project and event-identity guards, source-media protection,
backup-before-edit behavior, Undo, authentication, and output overwrite checks.
Changes to editing behavior need scenario tests and disposable-project readback.
Do not test against a work project or overwrite a running installation.

Never commit tokens, client configs with private data, discovery/runtime files,
logs, project files, media, dependencies, compiled output, or backups. Do not
redistribute ScriptPortal.Vegas.dll or any other proprietary VEGAS component.

The stable action contract is `docs/VEGAS-MCP-ACTION-CONTRACT.md`. Keep its path
and update its internal version/date when changing behavior. The build owns
`dist/UnitedTaps.VegasMcp.dll`; do not edit generated files.

Tests establish different things: Node tests cover transport/validation and
source invariants; the native harness checks the production range-edit algorithm
against a small timeline model. Neither proves every native VEGAS operation.
Report the exact build you checked and distinguish real host tests from models.
