# ADR-0159: Non-perturbing controlled audio render evidence

- Status: Accepted
- Milestone: 0.0.10d Phase 5
- Date: 2026-07-24

## Context

Two separately authorized physical commands used the same approved runtime, retained fixture, default endpoint policy, 100 ms audio buffer, operation bounds, and controlled production implementation.

The ordinary helper on clean main `d3cf3a32c66bb5a8dc322e6b1e524bd807c518b4` stopped with three outer delivery attempts, two delivered audio frames, zero retained WouldBlock, and a noise-like transient. The exact-result diagnostic on clean main `fcab3837722efb716decbc175e1dee678c939d9b` completed with four consecutive `AudioDelivered` results and accepted aggregate evidence, but the operator heard no sound.

The only repository change between the commands was documentation. The exact-result diagnostic nevertheless wrapped each outer delivery with an awaited continuation, lock acquisition, and list append before the finite composition could continue. That observation path may have changed the interval before the next decode or post-EOS scheduler delivery. The retained evidence therefore establishes a timing-sensitive or otherwise non-deterministic controlled boundary, but does not identify its cause.

Aggregate `AudioDelivered`, write acknowledgement, accepted frame and byte, and Start counters also do not establish that submitted frames remained queued long enough to be rendered by the endpoint.

## Decision

Add one bounded, opt-in controlled-audio render evidence boundary whose observations are produced inside the existing owners while their normal operations already hold the relevant serialization boundary.

### Scheduler and timing observation

The generation-matched delivery owner records at most four audio delivery observations. Each observation contains only:

- generation-scoped scheduler sequence;
- media kind;
- source presentation timestamp as an offset from the first observed source timestamp;
- timing decision result and bounded wait duration;
- final fixed generation-delivery result;
- whether the reservation was retained, delivered, or dropped.

The observation is committed under the delivery boundary's existing synchronization during normal completion. It adds no outer decorator, second queue, timer, sleep, polling operation, retry, reservation, or scheduler call.

### Native audio observation

The existing WASAPI write call already reads buffer capacity and current padding before every write. The native owner records at most four observations containing only:

- requested sample frames and bytes;
- buffer capacity frames;
- padding frames before the write;
- available frames before the write;
- whether Start was requested and acknowledged;
- whether GetBuffer and ReleaseBuffer were acknowledged;
- normalized fixed native result.

No additional native call is made for a write observation.

For the controlled evidence command only, cleanup may perform one read-only `GetCurrentPadding` query immediately before the existing `Stop` call. The query is not polling, does not delay by policy, and does not alter endpoint selection, buffer duration, sample format, submitted samples, or ordinary production behavior. Failure of the query is retained as an unavailable observation and does not replace the existing cleanup result.

### Hold and cleanup checkpoints

The finite controlled composition retains immutable snapshots:

- immediately after the required fourth delivery and before the observation hold;
- after the existing observation hold and before lifecycle cleanup;
- immediately before the controlled Stop call when available.

The snapshots permit comparison of accepted frames, Start state, padding, and remaining queued audio without publishing wall-clock time or payload data.

### Record and command

Add a new diagnostic-only canonical record and command. It uses the normal `SystemPlaybackControlledWindowsPhysicalRunOperations` directly and does not wrap `DeliverOneAsync`. It explicitly states:

- physical execution occurred;
- no audio acceptance is claimed;
- no external binary or payload was published.

The existing physical-run record and delivery-result diagnostic schemas remain unchanged.

## Bounds and privacy

- maximum scheduler/timing observations: 4;
- maximum native write observations: 4;
- maximum retained record size: 32 KiB;
- no runtime or record path;
- no device or endpoint identity;
- no handle, pointer, HRESULT, COM identity, thread identity, or wall-clock timestamp;
- no encoded packet, decoded sample, media payload, raw stdout, or raw stderr;
- no unbounded collection or counter.

Overflow or structural contradiction invalidates the diagnostic record.

## Non-effects

This decision does not change:

- normal production decode, scheduler, timing, reservation, conversion, or output behavior;
- endpoint selection, audio buffer duration, frame cadence, timing tolerance, fixture, runtime, or sample format;
- scheduler capacity, byte limit, overflow policy, or ownership;
- observation hold duration;
- retry, fallback, recovery, device recreation, synthesis, repetition, amplification, resampling, or silence insertion.

## Validation and physical gate

The implementation PR performs no physical output. It requires focused injected tests, Release build with analyzers and warnings as errors, full tests, format verification, and routine CircleCI.

No ordinary helper, prior delivery diagnostic, tone probe, decoded-slice probe, new render-evidence command, or paired verification is authorized during implementation. A later physical execution requires a newly merged main commit, final-head and post-merge CI success, new repository-external record paths, and a separate explicit one-run approval.
