# Original upstream public beta validation (historical)

For the current fork's native Build 31 results see [VEGAS2027-AUDIT.md](VEGAS2027-AUDIT.md).
The record below belongs to United Taps' original release and is not a claim
about testing performed by this fork.

Version: 2.1.0-beta.1
Date: 2026-10-16

The release was prepared as a separate source copy. The existing connector,
client configuration, installed DLL and active VEGAS project were not replaced.

## Checked locally

- Node 24.15.0 on Windows: syntax checks and 30 focused tests passed. These cover
  action catalog/routes, project and event guards, batch acknowledgement,
  instance discovery, redirected Documents, named-pipe responses, errors,
  disconnects and timeouts, plus selected source safety invariants.
- Real MCP stdio handshake: all nine tools and 100 actions were listed;
  capabilities returned structured data; missing project and range-delete
  acknowledgement were rejected before contacting VEGAS.
- The actual production range-delete method was compiled into a deterministic
  timeline-model harness. Twelve scenarios passed: split boundaries, preserved
  source offsets, all-track shifts, markers, regions, disabled marker shifting,
  acknowledgement, invalid ranges, and locked/grouped/automation refusals.
- The entire public C# extension compiled against the installed VEGAS Pro 2026
  scripting API. No proprietary API DLL is included in the repository.
- An isolated installer fixture passed first installation, token generation and
  protected permissions, token retention on update, DLL replacement, and rollback
  backup preservation. It used placeholder DLL bytes and a mocked process list
  under a disposable directory, not the actual VEGAS installation.
- The public Node server connected to the existing installed extension on VEGAS
  Pro 2026 Build 143 and read status, project and timeline data successfully.
  The host reported no project modification from those read-only calls.
- `npm audit --omit=dev` reported zero known vulnerabilities in the locked
  dependency tree on the validation date.

## What this does not establish

The model harness is not VEGAS. It does not establish native group rebuilding,
media decoding, fades, time stretching, automation interpolation or Undo.
Read-only connection checks used the existing installed DLL, not the new beta DLL.
The new DLL has not been installed or exercised through a full native editing,
Undo/Redo, snapshot and render regression session. The running work project was
not used for mutation tests.

The installer has been checked for refusing installation while VEGAS is running;
a complete installation on a separate clean Windows account is unverified.
CI checks Node 22/24 on Windows/Linux; its current result is visible on GitHub.
CI cannot run VEGAS or build against its proprietary API.

## Native acceptance before important work

Close VEGAS, install the beta, and reopen a saved disposable project with copied
test media. Verify insertion, a guarded trim/split, linked A/V groups, a small
batch, Undo/Redo, gain, one supported effect, a new snapshot and a short render.
Compare timeline readback with picture and sound in VEGAS. Exercise range-delete
boundary cases, markers/regions and its refusal paths; inspect all grouping and
automation after any acknowledged override. Verify the backup can be opened.

Do not interpret a passing source test or model scenario as exhaustive coverage
of 100 native actions, every plug-in, every media type or every render template.
