# PCN-0043 Media Signature Validation

## Scope

This report validates Milestone 0.0.5b Media Signature Classification. It
covers bounded recognition rules, reset and clearing behavior, fixed-field
diagnostics, finite session ownership, and one controlled real-media
classification against official PeerCastStation 6.0.0.0.

It does not retain media contents and does not claim codec detection,
decoding, rendering, A/V synchronization, or audible/visible playback.

## Automated results

- Playback tests: 29 passed
- CLI tests: 31 passed
- Foundation tests: 1 passed
- Core tests: 16 passed
- PCP Protocol tests: 43 passed
- Network tests: 81 passed
- Total: 201 passed
- Playback repetition gate: 20 runs / 580 executions passed
- CLI repetition gate: 10 runs / 310 executions passed
- Release build: zero warnings, zero errors
- `dotnet format --verify-no-changes`: passed
- TCP Transport self-test: passed
- PCP Framing self-test: passed

## Controlled official-upstream result

The pre-existing controlled channel was used without enumeration,
reconfiguration, process restart, or listener creation. The probe was limited
to 15 seconds, one attempt, no reconnect, a 64 KiB prefix, and four
discontinuities.

- Probe stop: `Recognized`
- Classification: `Recognized`
- Container: `FlashVideo`
- Basis: `FileHeader`
- Observed packets: 1
- Examined bytes: 506
- Discontinuities: 0
- Session end: `Stopped`
- Attempts / reconnects: 1 / 0
- HTTP status: 200
- Session packets / payload bytes: 2 / 13,414
- Classification-time skipped packets / bytes: 0 / 0
- Final pending / outstanding packets: 0 / 0
- Shutdown completed: true
- Classification gate: passed
- Process exit: 0

The record intentionally omits the Channel ID, endpoint, command line,
metadata, media bytes, signature bytes, packet positions, and session
identifiers.

## Covered boundaries

- split FLV headers and invalid reserved flags;
- Ogg resynchronization and unsupported structure version;
- three MPEG-TS sync intervals and rejection of only two;
- ASF Header Object GUID;
- bounded EBML DocType parsing for WebM and Matroska;
- unknown EBML DocType remains unknown;
- discontinuity clears the previous prefix before new classification;
- byte and discontinuity limits clear retained content and terminate;
- complete/dispose paths retain no prefix;
- invalid resource limits and post-terminal observation are rejected;
- CLI options, acceptance rules, fixed output schema, finite failure, and
  privacy surface are covered;
- owner cleanup after recognition is separated from classification-time
  latency-skip accounting.

## Decision

Milestone 0.0.5b Media Signature Classification is **Implemented**. Its
synthetic format rules, resource/lifecycle contracts, privacy boundary, and
controlled official-upstream FlashVideo classification are **Verified**.

Milestone 0.0.5 Playback remains in progress. The next boundary is a
replaceable decoder lifecycle that resets on every explicit discontinuity.

