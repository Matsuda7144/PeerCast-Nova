# ADR-0142: Explicit Controlled Windows Physical Run Composition

## Status

Accepted for milestone 0.0.10d Phase 4.

## Context

ADR-0139 added the fail-closed physical evidence gate, ADR-0140 connected sanitized native output counters, and ADR-0141 added the application-owned Windows runner. These parts were intentionally separate and could not yet execute one controlled external fixture through the approved lifecycle and submit a complete evidence record.

The composition must remain explicit, finite, and repository-external. A fixture digest mismatch must be discovered before any fixture byte reaches the parser or decoder. The application-owned window and dispatcher must remain live through playback cleanup. No hidden retry, delay, device fallback, runtime search, or resident loop is permitted.

## Decision

Add one explicit composition in `Nova.Cli` for one independent video or audio run, and expose it only through the dedicated `Nova.ControlledWindowsRunner` executable. Ordinary `Nova.Cli` startup remains unchanged.

The sequence is fixed:

1. validate explicit Windows runtime and fixture paths, exact fixture SHA-256, validation kind, and finite operation limits;
2. start the application-owned Windows runner;
3. initialize the approved external-runtime Windows lifecycle using the borrowed dispatcher and window token;
4. load the controlled FLV fixture within a fixed byte limit;
5. verify the complete fixture SHA-256 before exposing any packet;
6. replay finite callback-scoped PCP packet views into the lifecycle;
7. finalize encoded input once;
8. perform caller-driven receive or drain operations up to the configured limit;
9. perform at most the configured number of delivery attempts and stop after the first selected-media delivery or first contradiction;
10. capture the native buffer snapshot before cleanup;
11. stop and dispose the lifecycle through its existing production-first cleanup contract;
12. verify lifecycle state and both video／audio buffer, stream, and binding states reached `Disposed`;
13. prove the borrowed runner window and dispatcher remain usable;
14. submit the captured snapshot, counters, identity, and cleanup contract through the Phase 1 evidence gate;
15. stop the application runner.

### Fixture replay

`PcpControlledFlvFixtureReplayBoundary` reads one external fixture into bounded temporary memory, verifies its full SHA-256, then exposes sequential packet views only through an awaited callback. Digest mismatch exposes zero packets. The temporary payload is cleared after completion.

The replay boundary performs no network access, reconnect, seek, retry, fallback, or payload publication.

### Decode and delivery

Decode and delivery are bounded by immutable command options. `NeedMoreInput` and `StreamRequiresDrain` may transition once into finite drain calls. The composition adds no sleep, polling timer, resubmission, or retry.

A caller-visible `RetainedWouldBlock` is counted and terminates the run without retry. Evidence is accepted only after one selected-media physical acknowledgement.

### Cleanup evidence

The composition captures output counters before cleanup and submits cleanup ordering only after the reviewed lifecycle stop completes. The fixed lifecycle contract remains:

`production stop → buffer dispose → stream dispose → binding dispose → activation dispose`

The production operations then require the lifecycle plus both video／audio buffer, stream, and binding snapshots to report `Disposed`. Missing cleanup completion faults the run before evidence admission. The application runner must still report the original window token and successfully execute one owner-thread dispatcher operation after lifecycle cleanup.

### Command surface

The dedicated executable requires runtime directory, controlled fixture path, fixture digest, and `video` or `audio` kind. It handles Ctrl+C through caller-visible cancellation and propagates a fixed exit code. Output is sanitized and does not print local paths, device identifiers, pointers, handles, packets, or decoded bytes.

## Consequences

The repository now contains the complete finite composition and isolated entrypoint required to perform an independent controlled Windows video or audio run. The dedicated project is part of the solution and therefore covered by the routine build. Linux CI verifies the state machine, command admission, and fixture boundary with fake operations only; it does not establish physical output.

A later physical Windows execution must provide the repository-external approved runtime and retained fixture, run video and audio independently, and record accepted sanitized evidence. Until then, 0.0.10d remains physically unvalidated.
