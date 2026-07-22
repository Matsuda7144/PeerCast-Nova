# PCN-0155: Single Physical Run Record Preflight

## Milestone

0.0.10d Phase 5

## Review result

`ImplementedPhysicalVideoRevalidationPending`

## Problem

The controlled video helper retained the canonical physical record and validated its header, footer, milestone, kind, runtime build, fixture digest, and no-publication field. It did not immediately pass the full record through the canonical parser and the independent run-set admission boundary.

That left a gap between a successful physical command exit and the later paired-record verifier. A result or cleanup contradiction outside the helper's selected manual checks could remain undetected until after an audio run was attempted.

## Implemented boundary

- Added a shared bounded UTF-8 physical-record file reader.
- Refactored paired verification to use the shared reader without changing its report or admission behavior.
- Added `PlaybackControlledWindowsPhysicalRunRecordPreflightCommand`.
- Added `--verify-controlled-windows-run-record` dispatch to the dedicated runner.
- Required explicit record path, expected kind, and fixture SHA-256.
- Reused `PlaybackControlledWindowsPhysicalRunRecordCodec.TryParse` and `PlaybackControlledWindowsIndependentRunSetBoundary.ValidateAndConsume`.
- Accepted video only on `VideoAccepted` and audio only on `AudioAccepted`.
- Kept record paths out of all reports.
- Reported `Physical-Runs-Executed-By-This-Command: False`.
- Updated the video helper to run preflight after and only after a successful physical exit.
- Kept the canonical record outside the repository and removed the temporary preflight report.

No runtime, fixture, device, endpoint, window token, thread ID, native handle, packet, decoded payload, screenshot, or record path is published.

## Regression evidence

Focused tests verify:

- a valid video record returns `VideoAccepted` and exit zero;
- a valid audio record returns `AudioAccepted` and exit zero;
- expected-kind mismatch fails as an invalid record;
- completed-command but rejected evidence returns semantic failure;
- missing configuration fails before file access;
- reports never include repository-external record paths;
- preflight reports explicitly state that they executed no physical run.

Routine CI remains responsible for Release compilation with analyzers, full solution tests, and format verification.

## Remaining physical work

After merge, Codex must update the Windows checkout to the new main SHA and execute the controlled video helper once with the same approved runtime and retained fixture. A successful helper exit must include `Canonical video preflight: Accepted`, the external canonical record, and the separate operator observation `Video visible: Yes／No`.

Audio execution and paired verification remain blocked until the accepted video record and operator observation are reviewed.
