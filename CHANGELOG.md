# Changelog

## 2.1.1-vegas2027.31 - 2026-10-22

- Fork for Boris FX VEGAS Pro 2027.0.0 Build 31, retaining all nine tools and
  100 action routes from United Taps 2.1.0-beta.1.
- Exact executable/API file-version checks during build, install, native
  initialization/dispatch, discovery and explicit PID selection; registry discovery.
- x64 extension build, SHA-256 install manifest, retained token and DLL rollback
  backup, and process checks that work without WMI access.
- Fix Track Motion inspection on 2027 by cancelling a temporary native Undo
  context. Readback is verified to preserve project modification state and Undo/Redo.
- Unique project backup names, bounded idle/oversized pipe reads, and safe
  connection-only retries while the native listener reopens. Sent edits are not replayed.
- Zod 4.6.7 with the SDK's supported schema API; SDK remains at current 1.32.1.
- Codex STDIO setup with absolute paths and matching timeouts; exact-host launcher,
  doctor, stderr diagnostics, bounded native log, Russian setup instructions.
- Native saved-fixture acceptance for edits, groups, media, audio, OFX animation,
  Pan/Crop, Track Motion, snapshots, audio/video rendering and refusal paths.
- Installable ZIP packaging excludes proprietary API, tokens, private state,
  projects and backups. Original author notices and MIT license are retained.

See [the audit and exact validation boundary](docs/VEGAS2027-AUDIT.md).

## 2.1.0-beta.1 - 2026-10-16

First public source release, based on the existing United Taps local connector.

- Retains nine MCP tools and 100 domain actions, including the recent all-track
  range-delete action and title-generator parameter support.
- Portable launcher, Windows Known Folder discovery, configurable VEGAS build
  path, and installation instructions for other Windows accounts.
- Updates MCP SDK and the dependency lockfile; publication audit reports zero
  known vulnerabilities.
- Range-delete acknowledgement, valid project-range checks, locked/grouped
  event safeguards, automation acknowledgement, and marker/region mapping.
- Exact saved-project guards now cover transport commands too.
- Request deadlines prevent expired queued commands from starting. Native
  completion no longer signals a disposed wait handle after a timeout.
- Regression tests for transport, discovery, guards, and the range-edit
  algorithm; MCP stdio smoke and Windows/Linux CI.
- MIT license and sanitized documentation. Private runtime files, old local
  status notes, backups, unused script-bridge experiments, and media are excluded.

This beta has not received a full native editing/render regression run with its
newly built DLL. See `docs/VALIDATION.md` for the exact boundary.
