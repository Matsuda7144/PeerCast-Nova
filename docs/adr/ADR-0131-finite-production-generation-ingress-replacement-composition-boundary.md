# ADR-0131: Finite Production Generation Ingress and Replacement Composition Boundary

## Status

Accepted for milestone 0.0.9u.

## Context

ADR-0130 reviewed how the complete 0.0.9s production generation product can be connected to the awaited PCP／FLV path without violating decoded-completion ownership or allowing old and replacement native generations to overlap.

The existing `PlaybackPcpFlvAwaitedDecodeCompositionBoundary` is intentionally based on a consumed-settlement seam. Its success path still validates and disposes the decoded completion after settlement. Production scheduler ingress cannot use that seam because `PlaybackProductionDecoderGeneration.Transfer` detaches the managed frame and consumes the completion. Any later completion access would violate one-shot ownership.

Production composition also needs to serialize packet input, explicit finalization, one-frame receive／drain, scheduler transfer, one-item delivery, reset, discontinuity replacement, terminal cleanup, and stop while keeping the Windows output environment application-owned.

## Decision

### Dedicated production composition

Add `PlaybackProductionPcpFlvCompositionBoundary` as a new production-specific `IPcpChannelPacketConsumer`. Keep `PlaybackPcpFlvAwaitedDecodeCompositionBoundary` unchanged for diagnostic and consumed-settlement comparison.

The production boundary owns:

- one `PlaybackOneEventFinalEosFlvParser`;
- one production generation creator／factory adapter;
- at most one complete `PlaybackProductionDecoderGeneration`;
- one operation gate and one stop cancellation source;
- fixed lifecycle state, result, identity, generation, work, transfer, delivery, and copied-format counters.

It borrows one application-owned `PlaybackWindowsOutputBufferProcessingBoundary` and never activates, binds, initializes, or disposes the Windows device environment.

### Singular generation ownership and internal forwarding

The 0.0.9s product remains the singular owner of scheduler delivery, decode, native contexts, ABI gates, and verified runtime leases.

Add one assembly-internal lifetime-checked forwarding bridge for:

- exact decode snapshot observation;
- one-event packet／flush submission;
- one-frame ordinary, retained-relief, or flush-drain receive.

The bridge returns only existing finite managed results and completions. It never returns a raw decode boundary, native session, context pointer, ABI gate, runtime lease, or Windows resource.

For the concrete existing root, the bridge uses narrowly scoped `UnsafeAccessor` field access to reach the already-owned root, delivery, and decode boundary. Every call revalidates that the product, root, delivery, source session, decoder generation, and discontinuity generation are still exact and live. Injected test roots implement the same forwarding contract directly. This avoids creating a second lifetime owner or widening the public product surface.

### One serialized operation domain

One semaphore serializes:

- `ConsumeAsync`;
- `CompleteInputAsync`;
- `ReceiveDecodedAsync`;
- `DrainDecodedAsync`;
- `DeliverOneAsync`;
- `ResetAsync`;
- discontinuity replacement;
- terminal cleanup;
- `StopAsync`.

There is no resident loop, automatic scheduler pump, second encoded or decoded queue, capacity polling loop, or hidden retry.

### Parser and decode processing

PCP identity and exact packet sequence are checked before parser or replacement work. Callback payload remains callback-scoped. The parser advances only by exact `ConsumedByteCount`, and each source-owned packet or EOS completion is resolved before later payload suffix input.

Retained native packet／flush relief stays inside the open packet or finalization operation. Each relief iteration performs:

1. one source-bound receive;
2. one one-shot scheduler transfer when a frame is produced;
3. one retained submission retry.

The existing parser-operation, decode-operation, decoded-frame, and managed-byte limits remain reusable bounded budgets.

### Transfer is final ownership settlement

A decoded completion is validated before transfer against:

- exact source object;
- decoder and discontinuity generation;
- media role and stream ID;
- exact next stream-local sequence from the generation ingress snapshot;
- live frame role and exact managed byte count.

`PlaybackDecodedCompletionSchedulerIngressResult.Transferred` is the final ownership result. The composition increments its transfer count and never reads, validates, or disposes that completion again.

Cancellation before detach remains cancellation. Undefined, invalid, exhausted, faulted, or disposed transfer results are terminal. `EnqueueRejected` maps to a distinct terminal scheduler-admission result. The composition does not wait for capacity, reconstruct a frame, or retry admission.

### Caller-driven delivery and reset

`DeliverOneAsync` invokes exactly one 0.0.9q delivery operation outside the PCP callback. Delivered video／audio and deliberate video drop commit composition counters.

Wait, `WouldBlock`, initialization, binding, resize, audio recovery, clock-not-started, and reset-required results remain nonterminal and preserve the scheduler reservation in the generation root.

`ResetAsync` performs exactly one generation reset request. It does not activate or recreate application-owned Windows devices.

### Discontinuity replacement

After packet identity and sequence validation, replacement proceeds in this order:

1. detach the old generation from composition admission;
2. await old generation `StopAsync` completely;
3. invoke parser discontinuity reset, which disposes the old borrowed source session;
4. parse the replacement FLV description;
5. create and validate the replacement generation;
6. commit the PCP packet only after the complete operation succeeds.

Old and new native generations never overlap. Once old-generation stop begins, the old generation is never revived. Cancellation or failure after destructive stop is terminal; there is no rollback and no partial packet commit.

### Stop and terminal cleanup

Terminal failure closes admission, cancels linked operations, stops the active generation, disposes parser-owned source state, and disposes the factory adapter while preserving the first fixed terminal result.

`StopAsync` is idempotent, closes admission, cancels and awaits an active serialized operation, then performs the same ordered cleanup and records `Disposed`. Completed or active generations are stopped before parser and factory cleanup. The borrowed output boundary remains live.

### Fixed public state

The snapshot exposes only copied managed state:

- composition state and fixed result;
- accepted packet／byte counts;
- parser／decode operation counts;
- transferred／delivered／dropped frame counts;
- parser and decoder generation;
- active operation and decode-work flags;
- independent flush／decoder EOF flags;
- copied video width／height and audio sample rate／channel count.

It exposes no OS handle, COM pointer, native context, runtime path, or mutable source object.

## Rejected alternatives

- Mutating the existing consumed-settlement composition into production transfer semantics.
- Accessing or disposing a completion after successful scheduler transfer.
- Exposing the raw decode boundary or native session from the public generation product.
- Creating a parallel decoder or scheduler lifetime owner in the composition.
- Running delivery inside the PCP callback.
- Adding a resident pump, second queue, scheduler-capacity polling, or automatic admission retry.
- Resetting the parser before old generation stop completes.
- Overlapping old and replacement native generations.
- Reviving the old generation or rolling back after destructive replacement begins.
- Owning or recreating application-wide Windows resources in the composition.

## Consequences

- Production PCP／FLV ingress, one-shot scheduler transfer, finite delivery, explicit reset, and asynchronous replacement now share one bounded lifecycle owner.
- The existing diagnostic composition remains unchanged.
- Ten focused cross-platform tests cover pending creation, normal bootstrap and packet submission, retained relief transfer and retry, ordinary transfer, retained and committed delivery, terminal scheduler rejection, explicit finalization and independent drain, stop-before-create replacement, destructive cancellation, and borrowed-output cleanup.
- The pinned FFmpeg candidate remains `LicenseReview.Pending`, so the public default composition still fails closed when it first attempts generation creation.
- Normal backend registration, candidate approval, FFmpeg distribution, Windows device activation, and audible／visible playback remain outside this milestone.

## Next

0.0.9v — FFmpeg Candidate License, Backend Registration, and Distribution Review.
