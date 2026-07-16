# PCN-0047 Decoder Lifecycle Probe Validation

## Status

**Verified.**

The 0.0.5d diagnostic composition passed automated validation and a controlled
PeerCastStation real-media run. It verifies encoded-input lifecycle plumbing;
it does not claim that media was decoded, rendered, heard, or displayed.

## Automated validation

A Windows run using the repository-selected .NET 10 SDK completed:

- Release build with zero source warnings and zero errors;
- all 233 solution tests passed with zero failures and zero skips;
- Transport and PCP framing self-tests passed;
- all 13 decoder lifecycle tests passed for 20 repetitions, totaling 260
  executions;
- `dotnet format PeerCast-Nova.sln --verify-no-changes` completed with no
  output.

The repetition and controlled-probe wrappers now remove only nonexistent
process-local `LIB` entries before invoking `dotnet` and restore the original
environment afterward. This prevents stale machine configuration from producing
CS1668 warnings while preserving valid library search paths.

## Controlled-upstream result

The finite loopback probe was run against a user-selected channel already
available through PeerCastStation. The sanitized result was:

```text
Probe Stop: InputLimit
Classification: Recognized
Container: FlashVideo
Basis: FileHeader
Runner End: OwnerStopped
Submitted Inputs: 5
Submitted Bytes: 52914
Runner Resets: 0
Factory Creates: 1
Decoder Inputs: 5
Decoder Bytes: 52914
Decoder Resets: 0
Decoder Completes: 0
Decoder Disposes: 1
Cleanup Deferred: False
Deferred Cleanup Pending: False
Session End: Stopped
Attempts: 1
Reconnects: 0
HTTP: 200
Session Packets: 5
Session Payload Bytes: 52914
Buffer Skipped Packets: 0
Buffer Skipped Bytes: 0
Buffer Pending Packets: 0
Buffer Outstanding Packets: 0
Shutdown Completed: True
Lifecycle Gate: Passed
```

The configured post-recognition input limit was four. The runner submitted five
inputs because the one-time recognition/bootstrap submission is counted in
addition to the four post-recognition inputs. Runner and decoder input/byte
counters match exactly.

## Acceptance evidence

- recognized non-unknown container and fixed basis;
- runner end `OwnerStopped`;
- submitted inputs and bytes equal decoder inputs and bytes;
- runner resets equal decoder resets;
- one factory creation and one decoder disposal;
- cleanup deferred false and no deferred cleanup pending;
- HTTP 200 and session end `Stopped`;
- active skipped packets and bytes zero;
- final pending and outstanding packets zero;
- shutdown completed true;
- `Lifecycle Gate: Passed` and process exit 0.

The validation record omits Channel ID, endpoint, metadata, payload, prefix
bytes, positions, session identifiers, and exception text.

## Scope boundary

The CLI-internal probe decoder records callback counts and bytes and cooperates
with cancellation. No real decoder, codec dependency, decoded frame, renderer,
clock, audio device, video surface, or media output is included.

## Decision

ADR-0040 and the diagnostic composition are accepted and verified. Milestone
0.0.5d is complete. The next Playback milestone is the decoded output contract:
frame metadata, memory ownership, sink backpressure, discontinuity behavior,
and finite teardown before selecting a real codec backend.
