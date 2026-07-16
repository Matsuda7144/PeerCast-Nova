# PeerCast Nova 0.0.6c2-fix1 CI Summary

## Windows

The same restore, Release build, complete test, and format commands used by the
Windows matrix job were executed locally.

- Release build: zero warnings, zero errors.
- Tests: 289 passed, zero failed, one skipped.
- Explicit skip:
  `Locate_NonWindowsPlatformFailsClosedBeforeFilesystemAccess` —
  `Non-Windows fail-closed behavior is covered by Ubuntu CI.`
- Runtime locator/backend group: 48 passed and one explicit non-Windows skip
  per run; 20/20 runs completed (960 passes, 20 skips).
- Relay timing test
  `PcpRelayFanoutTests.Consume_SlowChildIsStoppedWithoutBlockingFastChild`:
  passed in the complete suite and 20/20 targeted runs.
- Format verification: passed.

This is a Windows CI-equivalent local result; the hosted `windows-latest` job
was not dispatched from this workspace.

## Ubuntu

The workflow retains an `ubuntu-latest` matrix job. Windows-only tests are
discovery-skipped with an explicit reason before `cmd.exe`, junction creation,
fixed-drive checks, or Win32 handles can execute. Ubuntu instead runs the
public fail-closed test requiring `PathRejected` before filesystem access.

Expected from the common discovery set:

- 277 passed;
- 13 Windows-only skips with an explicit reason;
- zero failed.

Actual Ubuntu execution was not available in this workspace: Docker was not
installed and WSL had no Linux distribution. Consequently Ubuntu CI success is
pending and is not claimed. The next authoritative evidence must be the hosted
`ubuntu-latest` job result.
