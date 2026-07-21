# ADR-0124: Decoded Completion Transfer and Scheduler Ingress

## Status

Accepted for milestone 0.0.9n.

## Context

Milestone 0.0.9m produces exactly one active `PlaybackSourceBoundDecodedFrameCompletion` and currently accepts only awaited `Consumed` settlement. The next production path must transfer that managed frame into the existing `BoundedPlaybackScheduler` without adding another decoded queue, double delivery, or a second frame copy.

The scheduler consumes the frame argument on every enqueue result: accepted frames become scheduler-owned, while rejected, dropped, invalid, oversized, sequence-rejected, or disposed paths dispose the incoming frame. Therefore ownership cannot be detached, enqueued, inspected, and then rolled back.

The decoded completion exposes stream-local video and audio sequences. The scheduler instead requires one strictly increasing sequence across both media kinds. Scheduler delivery also needs a decoder-context lease that the scheduler itself does not retain with each frame.

## Decision

### One-shot detach

Add a generation-controlled one-shot detach operation to `PlaybackSourceBoundDecodedFrameCompletion`. Detach succeeds only while the completion is the exact active completion, undisposed, source／decoder／discontinuity generation identity still matches, and ownership has not previously moved.

Successful detach atomically clears the completion's frame reference. Later completion disposal releases only the active-completion gate and must not dispose the transferred frame. A second detach, stale completion, generation mismatch, or contradictory state fails closed.

### Scheduler ingress transaction

Use one generation-scoped settlement adapter that validates the completion, reserves the next generation-wide downstream sequence, detaches exactly once, and calls `BoundedPlaybackScheduler.Enqueue` exactly once.

Only `Enqueued` and `EnqueuedAfterDroppingOldest` are accepted transfers. Because every other scheduler result has already consumed or disposed the detached frame, rejection is terminal for the decode generation. The frame is never reconstructed or retried.

The downstream sequence commits only after an accepted enqueue result. The stream-local decoded sequence remains part of source identity but is never passed directly as scheduler order. Overflow is terminal.

### Discontinuity

The first accepted frame of a replacement discontinuity generation carries `DecoderReset`; later frames carry `None`. The reset marker and downstream sequence commit together only after accepted ownership transfer.

A generation owns exactly one scheduler instance or one scheduler-generation owner. Old queued frames and any output reservation are disposed before the old decode boundary and native contexts. No old-generation frame may be delivered with a new-generation context lease.

### Context lifetime

Scheduler-owned managed frames remain associated with the exact decoder generation. The generation must keep matching video and audio context leases alive until all queued／reserved frames for that generation are delivered or disposed. The scheduler does not infer or own this context relationship.

### Failure and cancellation

Cancellation before detach commits nothing and leaves the completion owned by the composition for cleanup. Cancellation after accepted enqueue does not reclaim scheduler ownership. Stop, discontinuity, or terminal failure disposes the generation scheduler before the decode boundary.

Undefined enqueue results, sequence overflow, detach contradiction, scheduler disposal, and rejected enqueue terminate the generation. Direct awaited sink consumption and scheduler transfer are mutually exclusive settlement strategies for one frame.

## Rejected alternatives

- Copying the frame into a second composition queue.
- Passing video／audio stream-local sequences directly to the scheduler.
- Detaching before all identity and sequence checks are complete.
- Retrying or reconstructing a frame after rejected enqueue.
- Keeping old queued frames while replacing native decoder contexts.
- Delivering one frame both directly and through the scheduler.
- Treating scheduler enqueue as user-visible playback completion.

## Consequences

The next implementation can transfer each decoded frame once, preserve one bounded decoded queue, maintain strict cross-media order, and keep decoder context lifetime aligned with queued ownership. Milestone 0.0.9n changes documentation only. The next milestone is 0.0.9o One-shot Decoded Completion Transfer and Scheduler Ingress Boundary.