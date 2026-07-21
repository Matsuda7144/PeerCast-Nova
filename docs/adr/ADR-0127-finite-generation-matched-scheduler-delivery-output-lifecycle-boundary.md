# ADR-0127: Finite Generation-matched Scheduler Delivery and Output Lifecycle Boundary

## Status

Accepted for milestone 0.0.9q.

## Context

ADR-0126 selected scheduler reservation as the only safe production delivery foundation after 0.0.9o. The destructive scheduler run path removes ownership before output result is known, cannot retain a frame through Windows output backpressure, and accepts a caller-supplied context without exact source-generation proof.

The source-bound decode boundary already owns the exact opened H.264／AAC native session. The 0.0.9o ingress owns scheduler lifetime together with one decoder-generation lifetime. The remaining boundary had to connect those owners to timing, format adaptation／conversion, and result-aware output without introducing a second queue, resident pump, duplicate context ownership, or hidden retry.

The existing boolean scheduler reservation completion also counted every consumed reservation as delivery. Production late policy requires an explicit video drop to remove and dispose the reservation while incrementing drop accounting instead.

## Decision

### One finite generation owner

Add `PlaybackGenerationMatchedSchedulerDeliveryBoundary` as one exact source-bound generation owner. It retains immutable source-session, decoder-generation, discontinuity-generation, initial scheduler-sequence, decode-boundary, native-session, scheduler, and ingress identity.

The boundary creates the 0.0.9o ingress with the exact source-bound decode boundary as the singular generation lifetime. The ingress therefore disposes scheduler ownership before the decode boundary, and the decode boundary remains the only owner of the native video and audio contexts.

Construction rejects disposed or mismatched source, decode, native-session, generation, discontinuity, scheduler, or context-role state. Replacement generations begin at scheduler sequence zero.

### Finite transfer and delivery admission

`Transfer` delegates the irreversible decoded-completion transfer to the 0.0.9o ingress. Transfer and delivery are serialized at generation level; an active delivery does not detach a later decoded completion.

`DeliverOneAsync` performs at most one scheduler reservation and one final output attempt:

1. reserve the preferred media kind, falling back to the other kind through the existing scheduler;
2. validate exact source, decoder／discontinuity generation, scheduler sequence, frame liveness, media role, timestamp, and matching context role;
3. admit the one-shot decoder reset when required;
4. request one timing decision;
5. adapt an already canonical frame or convert a supported managed FFmpeg frame with the matching native context;
6. call one result-aware canonical output operation;
7. complete the reservation as delivered, dropped, or retained.

There is no internal wait loop, output retry, resident task, or second decoded queue. A retained result requires a later explicit caller operation.

### Matching context and result-aware output

Video frames select only the exact native session's H.264 video context. Audio frames select only the same session's AAC audio context. Context ownership is borrowed for the callback-scoped adapter／conversion operation and is never duplicated.

The production operations adapt or convert through the existing managed FFmpeg output boundaries and submit canonical BGRA32 video or interleaved S16／F32 audio to the existing Windows output buffer-processing boundary.

Output results remain explicit:

- processed video／audio: deliver and dispose the reservation;
- `WouldBlock`, initialization required, binding required, or resize required: retain;
- device loss: retain and enter reset-required state;
- invalid item, context loss, conversion failure, undefined result, or output failure: retain and fault the generation;
- cancellation: retain.

Device-wide Windows activation, binding, stream initialization, COM interfaces, and OS handles remain externally owned.

### Timing and late policy

A process-now timing result enters output once. A bounded wait retains the reservation without sleeping inside this boundary. Video late explicitly drops the reservation and advances scheduler drop accounting. Audio late retains the frame and reports audio recovery required. Clock-not-started retains without changing ownership.

Generation mismatch or discontinuity contradiction enters reset-required state. Invalid timing data or undefined timing results fault the generation.

### One-shot decoder reset acknowledgement

Only the initial scheduler sequence of a replacement generation may carry `DecoderReset`. Before timing or output, the boundary resets timing and generation-scoped output admission. Cancellation or reset failure leaves the reset marker and reservation retained.

After both resets succeed, the boundary acknowledges the marker exactly once and treats that same immutable reservation as discontinuity `None` for timing and output. Later reservations do not repeat the reset. A missing, repeated, or misplaced reset marker fails closed.

### Typed reservation completion

Add internal `PlaybackSchedulerOutputCompletion` values:

- `Retained`: release the reservation gate but keep scheduler ownership;
- `Delivered`: remove and dispose the frame and increment delivery count;
- `Dropped`: remove and dispose the frame and increment drop count.

The existing `Complete(bool)` API remains as a compatibility mapping to delivered or retained. Existing Windows scheduler, timed dispatch, and diagnostic callers therefore retain their previous behavior.

### Reset and asynchronous cleanup

`ResetAsync` cancels an active delivery, waits for reservation release, then resets timing and generation-scoped output admission. It does not dispose or recreate external device-wide Windows resources.

`StopAsync` closes transfer and delivery admission, cancels and awaits the active delivery, then disposes ingress ownership before generation operations. Ingress disposal drains scheduler frames before disposing the source-bound decode boundary and its native session. Repeated synchronous or asynchronous disposal is safe.

## Rejected alternatives

- Reusing destructive `BoundedPlaybackScheduler.RunOneAsync` for production delivery.
- Passing one arbitrary context lease for both media roles.
- Adding another decoder-context lifetime owner beside the source-bound native session.
- Treating deliberate video drop as successful delivery.
- Mutating the immutable scheduler reservation to remove `DecoderReset`.
- Retrying output or timing internally until success.
- Running a resident output pump inside the generation.
- Owning or disposing application-wide Windows output activation resources.

## Consequences

- Real scheduler-owned decoded frames now have one finite generation-matched path through timing, exact context conversion, and result-aware output.
- Retryable output and cancellation preserve scheduler ownership for a later explicit operation.
- Explicit video drop has correct scheduler accounting.
- Replacement-generation reset is admitted once without a repeated reset loop.
- Shutdown waits for active reservation release before native decoder ownership ends.
- Ten focused cross-platform tests cover delivery, drop accounting, retention, reset acknowledgement, cancellation, audio recovery, device loss, transfer serialization, asynchronous cleanup, and undefined results.
- Production generation factory, ABI-gate acquisition, backend activation, Windows device creation, runtime distribution, continuous pumping, and audible／visible playback remain outside this milestone.

## Next

0.0.9r — Production Decoder Generation Factory and Activation Review.
