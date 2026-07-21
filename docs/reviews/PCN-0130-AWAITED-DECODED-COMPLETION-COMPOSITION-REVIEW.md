# PCN-0130: Awaited Decoded Completion Composition Review

## Scope

Review the finite ownership connection between the 0.0.9i awaited PCP-to-FLV composition and the 0.0.9k source-bound one-frame receive／EOS-drain boundary. Define generation ownership, decoded-completion settlement, retained packet／flush relief, ordinary receive, independent drain, scheduler ingress, cancellation, discontinuity, and cleanup without adding runtime composition.

This milestone is documentation only. It does not change runtime source, tests, backend activation, FFmpeg distribution, Windows output lifecycle, or playback claims.

## Reviewed evidence

### Current awaited PCP composition

`PlaybackPcpFlvCompositionBoundary`:

- is the serialized awaited `IPcpChannelPacketConsumer`;
- keeps callback payload memory valid until exact parser suffix consumption finishes;
- owns the one-event FLV parser facade and one injected generation factory;
- creates a generation after `StreamDescriptionAvailable`;
- currently gives that generation a separate opened native session and finite submission state machine;
- calls the submission state machine's state-only `Advance` while packet or flush submission remains pending;
- returns from the callback only after parser bytes are consumed and no active submission remains;
- finalizes parser input explicitly rather than inferring EOS from transport state;
- currently treats accepted video and audio flush as enough for encoded-input finalization.

That generation model predates managed decoded ownership and cannot remain unchanged.

### Source-bound one-frame boundary

`PlaybackSourceBoundOneFrameDecodeBoundary`:

- owns the exact opened native session and one dedicated finite submission state machine;
- is the only native receive authority for its generation;
- returns one `PlaybackSourceBoundDecodedFrameCompletion` after one receive and one managed copy;
- carries exact source, decoder generation, discontinuity generation, media kind, stream ID, stream-local sequence, and managed-byte count;
- allows only one active decoded completion;
- delays retained packet／flush retry until that completion is released;
- separates ordinary receive, pending-submission relief, and flush drain;
- treats ordinary `NeedMoreInput` as normal and relief／drain `NeedMoreInput` as terminal no progress;
- tracks video and audio decoder EOF independently.

Adding this boundary beside the old generation submission state would duplicate ownership and is rejected.

### Existing downstream boundaries

`PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary`:

- serializes one awaited decoded sink callback;
- owns no output queue;
- leaves the managed input frame caller-owned;
- keeps converted payload valid only until the sink callback completes;
- maps cancellation, sink rejection, sink failure, conversion failure, and integration failure to fixed results.

`BoundedPlaybackScheduler`:

- is the existing count- and byte-bounded decoded-frame queue;
- owns accepted managed frames until delivery, dropping, reservation release, cancellation, or disposal;
- accepts one monotonic sequence shared across video and audio;
- disposes incoming frames on every nonaccepted enqueue result;
- uses the existing awaited sink-delivery boundary during caller-driven output;
- must receive the native context matching the queued managed FFmpeg frame when conversion or delivery occurs.

The 0.0.9k completion's video and audio sequences each begin independently, so they cannot be passed directly to the scheduler's cross-media sequence.

## Accepted architecture

### One generation, one decode authority

The future `PlaybackPcpFlvCompositionGeneration` is replaced or reshaped to own one `PlaybackSourceBoundOneFrameDecodeBoundary`. It no longer owns a separate public submission state machine or native session beside that boundary.

A successful generation records exact source and generation identity plus one decoded-completion settlement boundary and one generation-wide downstream sequence. The parser remains outer-composition-owned.

A later production factory performs temporary parameter binding and gate acquisition, creates and opens the native session, transfers the session into the decode boundary, and returns no partial successful owner. Production gate acquisition remains deferred.

### Awaited settlement contract

One injected generation-scoped asynchronous contract settles the exact active decoded completion.

While its `ValueTask` is incomplete, ownership remains active and all upstream advancement is blocked. A completed result is exactly one of:

- consumed by an awaited callback;
- transferred to an approved downstream owner;
- rejected;
- cancelled;
- faulted.

Consumed or transferred settlement releases the active completion. Rejected, cancelled, or faulted settlement terminates the generation and prevents retained packet／flush retry.

The contract must not return a “retained” state. Retention is represented by the awaited operation remaining incomplete.

### Relief inside the PCP callback

When encoded submission returns `ReceiveRequired`, the current PCP callback or explicit-finalization operation remains open.

One relief cycle is finite:

1. one native receive from the pending stream;
2. zero or one source-bound decoded completion;
3. one awaited settlement;
4. completion release after successful settlement;
5. one later retry-only relief transition.

Repeated `EAGAIN` requires another full cycle. Shared per-call budgets bound frame count, managed bytes, decoder transitions, and parser operations. No parser suffix or later PCP packet proceeds while relief ownership remains.

Decoder `NeedMoreInput` in relief is no progress and terminal. Decoder EOF completes only the pending stream and performs no packet retry.

### Ordinary output outside the callback

The PCP callback may return after all encoded bytes are consumed, parser completion is released, and no retained encoded submission or decoded completion remains. It does not stay open solely to drain arbitrary post-accept frames.

A separate serialized caller-driven operation performs at most one ordinary receive and one settlement. `NeedMoreInput` is normal. Multiple frames require multiple calls. No resident task or hidden loop is added.

### Finalization and drain

`CompleteInputAsync` remains explicit encoded-input finalization. It may emit and submit video then audio EOS and perform bounded relief needed for flush acceptance.

Accepted flush is not decoder EOF. Separate caller-driven drain operations receive and settle at most one frame for one selected stream. Video and audio independently reach decoder EOF. Drain `NeedMoreInput` is terminal no progress.

The composition must distinguish encoded finalization, decoder drain completion, and downstream ownership completion. It must not report full playback completion while scheduler frames remain queued or reserved.

### Scheduler ownership and sequence

The existing `BoundedPlaybackScheduler` remains the only permitted decoded queue. The composition does not add another decoded-frame collection.

The source-bound completion's stream-local sequence remains available for identity validation and diagnostics. A separate generation-wide checked downstream sequence is used for scheduler admission. It commits only after accepted transfer, ensuring strict cross-media monotonic order.

The reviewed scheduler transfer accepts only:

- `Enqueued`;
- `EnqueuedAfterDroppingOldest`.

All other enqueue outcomes are terminal for the composition. The production scheduler full policy remains a later activation decision.

A one-shot detach primitive is required because the current decoded completion owns its frame until disposal. Successful detach must prevent later completion disposal from clearing scheduler-owned data. Contradictory detach／enqueue outcomes fail closed.

### Context and discontinuity lifetime

Scheduler-owned managed FFmpeg frames may later require format conversion using the matching native decoder context. Therefore old-generation scheduled frames cannot be delivered through a replacement generation's context.

One scheduler ownership generation is paired with one decode generation. Explicit discontinuity:

1. closes old admission;
2. settles or cancels active decoded work;
3. disposes active decoded completion;
4. disposes old scheduler-owned frames;
5. disposes the old decode boundary and native session;
6. resets the parser and creates the new source／decode generation.

The first accepted downstream frame of the new discontinuity generation carries an explicit decoder-reset marker. The generation-wide scheduler sequence is fresh only with matching scheduler replacement; otherwise it must remain monotonically increasing. Silent sequence reuse is rejected.

### Direct sink delivery

The existing awaited sink-delivery boundary remains a valid no-queue settlement adapter. It must use the exact generation-owned context internally and may not expose that context through the public decoded completion.

A frame is settled either by direct awaited delivery or scheduler transfer, never both. The first implementation may inject a fake settlement contract and defer both real alternatives.

### Bounds and failure

Every outer operation owns finite limits. Relief shares frame and byte budgets across repeated cycles in the same callback or finalization call. Scheduler capacity and aggregate bytes remain separate downstream limits.

Cancellation before settlement commits no downstream sequence and performs no retained retry. Cancellation after accepted transfer does not reclaim scheduler ownership; terminal cleanup disposes the generation scheduler.

Undefined enums, source or generation mismatch, disposed source, contradictory active ownership, sequence overflow, consumer rejection, scheduler rejection, conversion failure, native failure, no progress, and budget exhaustion fail closed with fixed privacy-safe results.

### Cleanup order

Required relative cleanup order:

1. stop new operations and cancel active settlement;
2. dispose an untransferred active decoded completion;
3. dispose scheduler-owned or otherwise transferred decoded frames;
4. dispose the source-bound decode boundary, releasing pending native packet before parser completion and native audio before video;
5. dispose or reset parser／source ownership;
6. dispose factory-owned resources.

No callback payload, native frame pointer, context pointer, output device, or application scheduler loop becomes public completion ownership.

## Rejected alternatives

- Keeping the old submission state machine next to the 0.0.9k boundary.
- Using state-only receive for relief after managed completion support exists.
- Returning a PCP callback before decoded relief settlement.
- Retrying retained encoded ownership before completion release.
- Exhausting ordinary decoder output inside every PCP callback.
- Adding another decoded queue in the composition.
- Passing stream-local sequence directly to the cross-media scheduler.
- Reusing a replacement context for old scheduler-owned frames.
- Delivering one frame both directly and through the scheduler.
- Treating flush acceptance, decoder EOF, enqueue, or sink callback completion as the same state.

## Required 0.0.9m validation

The implementation milestone must include cross-platform injected tests for:

1. generation creation owning exactly one 0.0.9k boundary;
2. absence of state-only submission or receive advancement;
3. exact source, decoder generation, discontinuity generation, stream, and media validation;
4. one relief receive, one awaited settlement, and one retry-only transition;
5. PCP callback remaining incomplete while settlement is incomplete;
6. parser suffix and later PCP input blocked by active decoded ownership;
7. repeated packet `EAGAIN` under shared frame／byte／advance budgets;
8. repeated flush `EAGAIN` with the same ordering;
9. consumer rejection or cancellation preventing encoded retry;
10. relief `NeedMoreInput` terminal no progress;
11. ordinary one-frame receive outside the callback and normal `NeedMoreInput`;
12. explicit finalization separated from video／audio drain;
13. independent video and audio drain frames and EOF;
14. drain `NeedMoreInput` terminal no progress;
15. fixed encoded-finalized versus decoder-drained snapshots;
16. discontinuity cancelling settlement and disposing old ownership in order;
17. idempotent decoded-completion-first and decode-boundary-second cleanup;
18. no second decoded queue or resident loop.

Real scheduler transfer tests, cross-media scheduler sequence mapping, real awaited sink delivery, production gate acquisition, Windows output activation, and controlled playback remain later milestones unless explicitly included by a separately reviewed scope.

## CI

CircleCI remains the routine Linux merge gate. The final documentation-only branch head must pass restore, Release build, the full test suite, and format verification.

## Outcome

0.0.9l selects one awaited decoded-completion settlement seam and one generation-owned 0.0.9k decode authority. It keeps required relief inside the PCP callback, moves ordinary receive and EOS drain to finite caller operations, preserves the existing scheduler as the only decoded queue, and separates stream-local decoded identity from cross-media scheduler order.

Next milestone: **0.0.9m Finite Awaited Decoded Completion Composition Boundary**.