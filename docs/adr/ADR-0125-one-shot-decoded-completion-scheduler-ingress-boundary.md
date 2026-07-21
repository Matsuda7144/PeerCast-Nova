# ADR-0125: One-shot Decoded Completion Scheduler Ingress Boundary

## Status

Accepted for milestone 0.0.9o.

## Context

ADR-0124 established that `BoundedPlaybackScheduler.Enqueue` is an irreversible ownership boundary. Every enqueue result consumes or disposes the incoming managed frame, so decoded ownership must be validated completely, detached once, and enqueued once without rollback, reconstruction, or retry.

`PlaybackSourceBoundDecodedFrameCompletion` previously represented ownership only through a nullable frame reference. Moving that reference outside the completion without coordination with `PlaybackSourceBoundOneFrameDecodeBoundary` could bypass the exact-active-completion gate, race completion disposal, or allow a stale generation to transfer a frame.

The source-bound decode boundary also exposes independent video and audio sequences, while the scheduler requires one monotonic cross-media sequence. Replacement generations require one `DecoderReset` marker and must keep their decoder-context lifetime alive until scheduler-owned frames have been released.

## Decision

### Explicit completion ownership state

`PlaybackSourceBoundDecodedFrameCompletion` now distinguishes attached, detached, and disposed frame ownership.

- Attached means the exact active completion still owns the managed frame.
- Detached means frame ownership has moved once and the completion can no longer expose or dispose that frame.
- Disposed means the completion has released its active-completion gate.

A generation-controlled detach asks the owning `PlaybackSourceBoundOneFrameDecodeBoundary` to validate the exact active completion before the ownership state changes. The boundary checks source identity, decoder generation, discontinuity generation, media kind, stream ID, stream-local sequence, completion mode, managed-byte count, frame liveness, and frame media role.

Successful detach changes attached to detached exactly once and removes the frame reference. Later completion disposal releases only the decode backpressure gate. A second detach, concurrent disposal, stale completion, or contradictory identity fails closed.

### Scheduler ingress boundary

Add `PlaybackDecodedCompletionSchedulerIngressBoundary` as one generation-scoped owner of:

1. the existing `BoundedPlaybackScheduler`;
2. the matching decoder-context lifetime;
3. the next expected video and audio source sequences;
4. one generation-wide scheduler sequence;
5. the pending first-frame `DecoderReset` marker.

`Transfer` performs one finite transaction:

1. reject cancellation before detach without changing completion ownership;
2. validate the exact completion and both source-sequence identities;
3. reject scheduler-sequence exhaustion before detach;
4. detach the managed frame once;
5. dispose the completion to release decode backpressure;
6. call scheduler enqueue once;
7. commit source-admission sequence, scheduler sequence, accepted-transfer count, and reset-marker consumption only after `Enqueued` or `EnqueuedAfterDroppingOldest`.

The stream-local sequence is retained only as source identity. It is never passed to the scheduler. The scheduler receives the separate generation-wide sequence.

An injected enqueue operation exists only as a cross-platform focused-test seam. The production operation delegates directly to `BoundedPlaybackScheduler.Enqueue` and does not own another queue.

### Failure and cancellation

Cancellation before detach returns `Cancelled`, leaves the generation accepting, and leaves the completion attached for caller cleanup or a later explicit transfer.

Sequence exhaustion, invalid completion, repeated transfer, undefined enqueue result, rejected enqueue, or enqueue exception is terminal for the ingress generation. No rejected frame is reconstructed or retried.

If an injected enqueue operation throws before consuming the detached frame, the ingress disposes that frame. If enqueue returns, the scheduler contract is treated as having consumed the frame regardless of result.

### Discontinuity and context lifetime

A discontinuity generation greater than zero begins with `DecoderReset` pending. Only the first accepted enqueue consumes that marker. Cancellation and all pre-detach validation failures do not consume it.

The ingress owns the scheduler and matching decoder-context lifetime together. Disposal and terminal cleanup always release scheduler-owned frames first and then release the decoder-context lifetime. The outer generation remains responsible for disposing any active completion before disposing this ingress and for disposing the decode boundary／native session after it.

### Scope

The existing direct awaited-consumption path in `PlaybackPcpFlvAwaitedDecodeCompositionBoundary` remains unchanged. This milestone provides the reviewed one-shot detach and scheduler-ingress runtime boundary; it does not activate a production generation factory, scheduler delivery, output lifecycle, backend activation, FFmpeg distribution, or audible／visible playback.

## Rejected alternatives

- Accessing the completion's private frame field through `UnsafeAccessor`.
- Treating a null frame reference as both detached and disposed ownership.
- Detaching without confirmation from the exact active decode boundary.
- Passing independent video／audio sequences directly to the scheduler.
- Committing sequence or reset state before accepted scheduler ownership.
- Retrying, reconstructing, or copying a rejected frame.
- Adding a second decoded queue or resident scheduler pump.
- Releasing decoder contexts while scheduler frames remain owned.

## Consequences

- Managed frame ownership can cross from decode completion to scheduler exactly once.
- Completion disposal after detach releases backpressure without double-disposing the scheduler-owned frame.
- Scheduler order is monotonic across media while stream-local identity remains independently checked.
- The first accepted replacement-generation frame carries exactly one reset marker.
- Terminal cleanup preserves scheduler-before-context order.
- Eleven focused tests cover detach, cross-media ordering, cancellation, reset commit, rejection, identity mismatch, repeated transfer, exhaustion, undefined results, exceptions, and ordered disposal.
- The next milestone is 0.0.9p Generation-matched Scheduler Delivery and Output Lifecycle Review.
