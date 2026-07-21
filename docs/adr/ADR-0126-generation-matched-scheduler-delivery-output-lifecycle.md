# ADR-0126: Generation-matched Scheduler Delivery and Output Lifecycle

## Status

Accepted for milestone 0.0.9p architecture review.

## Context

0.0.9o transfers one exact source-bound decoded completion into the existing `BoundedPlaybackScheduler`. The next boundary must deliver those scheduler-owned frames without allowing an old frame to use a replacement decoder context or output generation.

The repository currently has two different scheduler exit paths:

1. `BoundedPlaybackScheduler.RunOneAsync` destructively dequeues one frame and calls `PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary` with a caller-supplied decoder context lease.
2. The internal scheduler reservation path keeps the item scheduler-owned until an output adapter explicitly completes the reservation.

The first path is useful for finite diagnostics, including managed format conversion, but it cannot retain a frame after output backpressure because the frame has already been dequeued and is disposed when the operation returns. It also does not bind the supplied context lease to the frame's decoder generation.

The reservation path is used by the Windows output and late-aware dispatch boundaries. It can retain the same item through bounded waits, `WouldBlock`, resize, device loss, and recovery decisions. However, those boundaries currently accept only already-canonical BGRA32 or interleaved S16/F32 frames and do not select the matching FFmpeg video or audio context for conversion.

A further ownership gap exists at the 0.0.9o seam. `PlaybackDecodedCompletionSchedulerIngressBoundary` owns a scheduler and a generic decoder-context lifetime, while the real video and audio context leases are owned inside `PlaybackBoundedFlvNativeDecoderSession`, which is retained by `PlaybackSourceBoundOneFrameDecodeBoundary`. Production composition must not create a second owner for those same native contexts.

The first accepted frame of a replacement generation carries `DecoderReset`. Existing late-aware timing treats any non-`None` discontinuity as reset-required and retains the reservation. Re-evaluating the same immutable reservation without an explicit reset acknowledgement would repeat that result indefinitely.

Finally, the scheduler reservation currently completes with a boolean. `consume: true` increments the delivery count even when a late video frame is intentionally dropped, so delivery and explicit drop are not distinguishable at the generation boundary.

## Decision

### One generation owner

The next implementation will introduce one generation-scoped owner containing the exact source and generation identity, the source-bound decode boundary and native session, the 0.0.9o scheduler ingress, the scheduler delivery gate, and generation-scoped output lifecycle state.

The opened native session remains the sole owner of the video and audio `PlaybackDecoderFfmpegContextLease` instances. No second context lease, duplicate native-session owner, or independent decoder lifetime will be created.

The 0.0.9o generic decoder-context lifetime must be integrated as transferred ownership inside this composite rather than passed as a second separately disposed reference. The generation owner may retain non-owning internal references for exact context selection, but cleanup authority remains singular and idempotent.

### Reservation-based delivery only

Production generation delivery will use the scheduler reservation path. `BoundedPlaybackScheduler.RunOneAsync` remains a diagnostic API and will not be used by the production generation composition.

Each public delivery operation is finite and caller-driven:

1. reserve at most one scheduler item;
2. validate exact generation, sequence, media kind, frame liveness, and discontinuity state;
3. choose the matching video or audio context from the same native decoder session;
4. perform bounded timing and reset admission;
5. adapt or convert the frame while the exact context remains live;
6. submit the canonical callback-scoped payload through one result-aware output operation;
7. explicitly deliver, drop, or retain the reservation.

No resident delivery loop, background task, hidden retry, or second decoded queue is introduced.

### Result-aware canonical output seam

The generation delivery boundary will not use `IPlaybackDecodedFrameSink` as its sole output contract because that callback cannot represent `WouldBlock`, initialization required, resize, device loss, recovery, or retryable rejection.

Instead, the next implementation will define an injected result-aware canonical output seam. The production adapter will call the existing Windows buffer-processing boundary; focused cross-platform tests will use injected operations. Managed FFmpeg adaptation and conversion remain callback-scoped, and no converted payload is retained after the awaited output operation returns.

Only a successful video or audio process result completes the reservation as delivered. Retryable output results retain it. Device loss, reset requirements, contradictory results, and integration failures return fixed terminal or reset-required outcomes without pretending that output succeeded.

The pure awaited sink and renderer／audio sink boundaries remain available for diagnostics and alternate future adapters, but one frame is never sent through both those paths and the Windows reservation path.

### Exact context and generation matching

A reserved video frame may use only the video context from its own source-bound native session; a reserved audio frame may use only the matching audio context. The generation owner validates its exact source session, decoder generation, discontinuity generation, scheduler sequence, media role, and live context before conversion or output.

Old-generation reservations must be cancelled and released before a replacement generation becomes eligible for delivery. A context from the replacement generation is never used to convert an old scheduler frame.

The initial implementation uses one outer generation identity to configure both output media generations. Independent audio recovery generation changes remain a later explicitly reviewed feature.

### DecoderReset admission

Only the first accepted scheduler frame of a replacement generation may carry `DecoderReset`.

The generation delivery owner holds a one-shot reset-admission state:

- before successful output/timing reset, the reservation remains owned and the reset marker remains pending;
- cancellation or retryable output does not consume the marker;
- after successful reset admission, the same reservation is evaluated as the first ordinary frame of the new generation without mutating the stored scheduler frame;
- repeated `DecoderReset`, a reset marker on a non-first scheduler sequence, or a contradictory generation is terminal;
- only successful reset admission commits the marker as consumed.

This prevents an immutable first reservation from repeatedly returning reset-required forever.

### Reservation completion semantics

The generation delivery contract distinguishes three outcomes:

- `Delivered`: remove and dispose the item and increment delivery accounting;
- `Dropped`: remove and dispose the item and increment drop accounting;
- `Retained`: release the reservation without removing the item.

The next implementation must add an explicit completion enum or an equivalent typed scheduler method for this path. It must not encode a deliberate video drop as delivery.

### Output lifecycle ownership

Windows activation, resource binding, stream initialization, COM interfaces, OS handles, and device-wide ownership remain outside the decoder generation and may be shared by a higher application owner.

The decoder generation owns only its output admission state, exact output-generation identity, active reservation, reset progress, and in-flight conversion／submission operation. It borrows the external output boundary for one finite call and never disposes global device resources.

Output configuration or initialization occurs only through an explicit owner operation after enough canonical media metadata is known. It is not started implicitly by enqueue or by a resident pump.

### Cancellation, stop, and cleanup

The generation owner will implement asynchronous shutdown because scheduler disposal alone cannot synchronously dispose an active reservation.

Replacement, terminal failure, stop, and disposal use this order:

1. close decode ingress and delivery admission;
2. cancel and await the active delivery operation so its reservation is completed exactly once;
3. dispose any active decoded completion;
4. dispose scheduler reservations and queued scheduler-owned frames;
5. dispose the source-bound decode boundary and its native audio／video contexts;
6. release generation-scoped output reset／admission state;
7. leave global Windows activation, binding, initialization, and device resources with their external owner.

No context is disposed while conversion, an awaited output callback, or a scheduler reservation can still access its frame.

## Rejected alternatives

- Using `BoundedPlaybackScheduler.RunOneAsync` for production generation delivery.
- Supplying an arbitrary caller-owned decoder context to a scheduler-owned frame.
- Creating another native-session or context owner beside the source-bound decode boundary.
- Converting after releasing or replacing the matching decoder generation.
- Treating the existing boolean reservation completion as sufficient for both delivery and explicit drop.
- Passing `DecoderReset` repeatedly through the unchanged timing boundary.
- Consuming a reservation on `WouldBlock`, resize, device loss, cancellation, or recovery-required results.
- Sending the same frame through both awaited-sink and Windows output paths.
- Adding a second decoded queue, resident output loop, or implicit retry.
- Disposing global Windows device resources as part of decoder-generation replacement.

## Consequences

- Production output will preserve scheduler ownership across retryable timing and device results.
- Managed FFmpeg conversion will always use the exact media context from the frame's decoder generation.
- Decoder context ownership remains singular even though delivery borrows the exact context.
- The first replacement-generation reset marker can be acknowledged once without an immutable-reservation reset loop.
- Delivered, dropped, and retained scheduler outcomes become separately accountable.
- Generation replacement requires awaited cleanup rather than synchronous scheduler disposal alone.
- 0.0.9p changes documentation only. Runtime composition, output activation, backend activation, FFmpeg distribution, and audible／visible playback remain unchanged.
- The next milestone is 0.0.9q Finite Generation-matched Scheduler Delivery and Output Lifecycle Boundary.
