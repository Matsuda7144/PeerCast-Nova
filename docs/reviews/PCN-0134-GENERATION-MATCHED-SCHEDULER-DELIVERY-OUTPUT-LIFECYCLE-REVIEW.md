# PCN-0134: Generation-matched Scheduler Delivery and Output Lifecycle Review

## Milestone

0.0.9p — Generation-matched Scheduler Delivery and Output Lifecycle Review

## Reviewed boundaries

- `PlaybackDecodedCompletionSchedulerIngressBoundary`
- `PlaybackSourceBoundOneFrameDecodeBoundary`
- `PlaybackBoundedFlvNativeDecoderSession`
- `BoundedPlaybackScheduler`
- `PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary`
- `PlaybackWindowsOutputSchedulerAdapterBoundary`
- `PlaybackLateAwareTimedDispatchBoundary`
- `PlaybackWindowsOutputBufferProcessingBoundary`
- `PlaybackWindowsOutputStreamInitializationBoundary`
- renderer／audio sink boundaries

## Findings

1. `BoundedPlaybackScheduler.RunOneAsync` removes ownership before the final output result is known. It cannot retain a frame through Windows `WouldBlock`, resize, device loss, or recovery, and it accepts a decoder context lease without source-generation proof.
2. The internal reservation path preserves scheduler ownership and is the only suitable production delivery foundation.
3. Existing Windows scheduler adapters operate only on canonical BGRA32 or interleaved S16/F32 frames. They do not select the exact matching FFmpeg video／audio context or run managed conversion.
4. `IPlaybackDecodedFrameSink` cannot express the retryable and reset-aware results required by Windows output lifecycle. A generation implementation needs a result-aware canonical output seam.
5. The real video and audio context leases are owned by the source-bound native decoder session. A second context-lifetime owner beside that session would create ambiguous cleanup authority.
6. The scheduler's active output reservation survives scheduler disposal until the reservation completes. Generation replacement therefore requires cancellation and awaited reservation release before native contexts are disposed.
7. The first accepted replacement-generation frame has immutable `DecoderReset`. Passing it unchanged to the current timing boundary after every reset would repeatedly return reset-required. The generation needs a one-shot reset-admission acknowledgement.
8. Boolean reservation completion merges successful delivery and deliberate drop accounting. The production generation path needs delivered／dropped／retained outcomes.
9. Windows activation, binding, initialization, COM, and OS handles are device-wide resources. Decoder generation replacement must not dispose them.
10. Direct awaited-sink delivery and Windows reservation delivery are mutually exclusive choices for one scheduler frame.

## Selected 0.0.9q shape

Add one finite generation-matched delivery owner that:

- retains exact source, decoder generation, and discontinuity generation identity;
- owns the source-bound decode generation through one singular ownership chain;
- owns the 0.0.9o scheduler ingress and scheduler delivery admission;
- reserves at most one item per caller-driven operation;
- validates exact scheduler sequence, media role, context liveness, and generation;
- borrows only the matching native video or audio context;
- adapts／converts callback-scoped frame data;
- calls one injected result-aware canonical output operation;
- explicitly delivers, drops, or retains the reservation;
- consumes the first-frame reset marker only after successful reset admission;
- stops without hidden retry, resident pump, or second queue.

The production output adapter will target the existing Windows buffer-processing boundary. Cross-platform focused tests will use injected operations and will not activate Windows devices.

## Delivery result rules

- successful video／audio processing: delivered, reservation removed, delivery accounting advanced;
- explicit policy video drop: dropped, reservation removed, drop accounting advanced;
- bounded wait, `WouldBlock`, initialization／binding／resize requirement, cancellation, and audio recovery request: retained;
- generation contradiction, repeated reset marker, undefined result, context loss, conversion integration failure, or output failure: reset-required or terminal according to the fixed result contract;
- no rejected item is copied, reconstructed, or enqueued again.

## Reset and output lifecycle

- only scheduler sequence zero in a replacement generation may carry `DecoderReset`;
- successful generation-output reset acknowledges it once;
- cancellation and failed reset do not consume it;
- the same reservation may then continue as an ordinary first frame without mutating scheduler storage;
- video and audio initially use one outer generation identity;
- independent audio recovery generation changes remain deferred;
- Windows activation／binding／stream initialization ownership stays external.

## Cleanup order

1. close decode and delivery admission;
2. cancel and await active delivery／reservation completion;
3. active decoded completion;
4. scheduler reservation and queued frames;
5. source-bound decode boundary and native audio／video contexts;
6. generation-scoped output admission／reset state;
7. external device-wide Windows resources remain owned by their application owner.

This requires an asynchronous generation disposal boundary. Synchronous scheduler disposal alone is insufficient while a reservation is active.

## Scope guard

This review changes no runtime source or tests. It does not implement the generation composite, alter scheduler reservation APIs, activate FFmpeg, acquire production ABI gates, activate Windows resources, distribute a runtime, start a continuous pump, implement complete synchronization, or claim audible／visible playback.

## Next

0.0.9q — Finite Generation-matched Scheduler Delivery and Output Lifecycle Boundary.
