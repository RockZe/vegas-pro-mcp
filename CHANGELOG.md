# Changelog

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
