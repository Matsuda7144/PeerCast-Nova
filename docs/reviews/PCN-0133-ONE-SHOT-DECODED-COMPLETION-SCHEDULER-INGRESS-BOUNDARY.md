# PCN-0133: One-shot Decoded Completion Scheduler Ingress Boundary

## Milestone

0.0.9o — One-shot Decoded Completion Transfer and Scheduler Ingress Boundary

## Implemented boundaries

- `PlaybackSourceBoundDecodedFrameCompletion`
- `PlaybackSourceBoundOneFrameDecodeBoundary`
- `PlaybackDecodedCompletionSchedulerIngressBoundary`
- `BoundedPlaybackScheduler`
- generation-matched decoder-context lifetime ownership

## Implementation findings

1. A nullable completion frame alone cannot distinguish transferred ownership from disposed ownership. The completion now has explicit attached／detached／disposed states.
2. The exact decode boundary must authorize detach while the completion is still its active completion. Private-field access was removed rather than accepted as an ownership API.
3. Completion disposal after successful detach releases decode backpressure without touching the transferred managed frame.
4. Video and audio source sequences remain independently checked from zero, while scheduler admission uses a separate generation-wide sequence.
5. Cancellation before detach is non-terminal and leaves both completion ownership and the first-frame reset marker unchanged.
6. Scheduler enqueue remains one-shot. Only `Enqueued` and `EnqueuedAfterDroppingOldest` commit source-admission sequence, scheduler sequence, accepted count, and `DecoderReset` consumption.
7. Rejection, undefined result, exhaustion, identity contradiction, repeated transfer, or enqueue exception faults the ingress generation and disposes scheduler ownership before decoder-context lifetime.
8. No second decoded queue, resident loop, frame reconstruction, scheduler retry, output activation, or production factory was introduced.

## Focused validation

Eleven focused tests cover:

- exact-active one-shot detach;
- detached completion disposal without frame double-disposal;
- decode backpressure release;
- video／audio stream-local zeroes mapped to scheduler sequences zero and one;
- cancellation before detach;
- first accepted replacement-generation `DecoderReset`;
- bounded scheduler rejection and no sequence commit;
- generation mismatch;
- repeated transfer;
- scheduler-sequence exhaustion;
- undefined enqueue result;
- enqueue exception ownership cleanup;
- scheduler-before-context disposal order.

The tests use the real `BoundedPlaybackScheduler` for ownership behavior and one injected enqueue seam only for call counting, undefined-result, and exception cases.

## Cleanup order

Within the scheduler-ingress owner:

1. scheduler queue and scheduler-owned frames;
2. matching decoder-context lifetime.

The outer generation contract remains:

1. active decoded completion;
2. scheduler ingress ownership;
3. decode boundary and retained submission state;
4. native decoder session.

## Scope guard

`PlaybackPcpFlvAwaitedDecodeCompositionBoundary` remains on its direct awaited-consumption settlement path. This milestone does not claim generation-matched scheduler delivery, output reservation, Windows output lifecycle, production ABI-gate acquisition, backend activation, runtime distribution, or audible／visible playback.

## Next

0.0.9p — Generation-matched Scheduler Delivery and Output Lifecycle Review.
