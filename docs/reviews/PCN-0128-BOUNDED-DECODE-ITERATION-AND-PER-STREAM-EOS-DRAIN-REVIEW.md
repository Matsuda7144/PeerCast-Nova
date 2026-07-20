# PCN-0128: Bounded Decode Iteration and Per-stream EOS Drain Review

## Scope

Review the finite decoded-output boundary required after 0.0.9i packet／flush submission. Define how one native frame receive becomes one source-bound managed-frame owner, how retained sends are relieved without losing frames, and how video and audio drain independently after accepted null-packet flush.

This milestone changes documentation only. It does not add runtime decode iteration, managed-frame delivery, scheduler ingress, a production generation factory, backend activation, output wiring, or audible／visible playback.

## Reviewed evidence

### Current submission receive path

- `PlaybackFiniteOneEventPacketSubmissionStateMachine.Advance` delegates one receive attempt to `IPlaybackFiniteOneEventPacketSubmissionOperations.ReceiveOne`.
- The production operation calls `PlaybackDecoderFfmpegPacketFrameBoundary.ReceiveFrame` and receives only `FrameAvailable`, `NeedMoreInput`, EOF, or fixed failure states.
- That state-only boundary allocates and frees one temporary `AVFrame` inside the call.
- A `FrameAvailable` result therefore proves frame progress but does not preserve frame metadata or bytes.
- Calling another receive boundary afterward would consume the next decoder output, not the frame already released.

### Existing callback-scoped frame ownership

- `PlaybackDecoderFfmpegNativeFrameBoundary` keeps one `AVFrame` valid only inside one consumer callback and frees it before returning.
- `PlaybackDecoderFfmpegFrameMetadataBoundary` validates fixed H.264／AAC frame metadata and converts native frame timestamps to managed time values.
- `PlaybackDecoderFfmpegFrameDataBoundary` validates callback-scoped plane／sample pointers, strides, rows, logical lengths, and a 256 MiB hard referenced-byte bound.
- `PlaybackDecoderFfmpegManagedFrameCopyBoundary` copies logical frame data into one independently owned managed frame and clears owned buffers on disposal.
- Those boundaries currently operate on a context lease only; they do not attach exact parser source identity, stream ID, decoder generation, decoded sequence, or drain state.

### Current EOS state

- The one-event submission state machine tracks video and audio flush acceptance independently.
- It also tracks decoder EOF observed while relieving an active submission.
- A flush accepted without immediate EOF is not followed by later drain receives.
- 0.0.9i `CompleteInputAsync` verifies accepted flush or observed EOF for both streams, but that is encoded-input finalization rather than proof that every delayed decoded frame was received.

## Accepted architecture

### Sole receive authority

The 0.0.9k implementation must add one source-bound receive／drain boundary owned by the exact decoder generation. It is the only production component allowed to call `avcodec_receive_frame` for those contexts.

The production pending-send path must not use the state-only receive boundary and then call the managed-copy boundary. One receive operation must invoke the callback-scoped native frame path and finish with exactly one of:

- one owned source-bound managed decoded completion;
- normal `NeedMoreInput` in ordinary post-accept mode;
- decoder EOF for the selected stream;
- a fixed terminal failure.

Each public operation performs at most one native receive.

### Source-bound decoded completion

One copied frame is wrapped with:

- exact source-session reference;
- decoder generation;
- exact media kind and stream ID;
- independent per-stream decoded sequence;
- discontinuity generation marker;
- one owned `PlaybackDecoderFfmpegManagedFrame`.

Only one decoded completion may remain active across the generation. No additional receive, packet／flush retry, parser input, or PCP callback may advance until that completion is consumed or disposed.

Encoded packet sequence is not reused as decoded sequence. The frame retains both validated presentation and best-effort timestamps; no timestamp is synthesized in this milestone.

### Pending packet／flush relief

A retained submission keeps the PCP or explicit-finalization operation open.

For one relief step:

1. receive at most one frame from the pending stream context;
2. expose that frame through one active decoded completion;
3. retry the retained packet／flush at most once only after frame ownership is accepted and released from the receive boundary.

A produced but unaccepted frame blocks the retry and preserves the retained encoded submission. `NeedMoreInput` is no progress in this mode and is terminal. Decoder EOF completes the selected stream through the submission state machine's fixed transition.

Relief work must have independent frame-count, decoder-advance, and cumulative managed-byte budgets per PCP／finalization call.

### Ordinary post-accept receive

After packet acceptance, a separately driven finite operation may select video or audio and receive one frame.

- One frame returns one active decoded completion.
- `NeedMoreInput` is normal and ends that finite operation without failure.
- EOF marks only the selected stream complete.
- Multiple output frames require multiple caller-driven operations.
- The PCP callback need not remain open solely to drain post-accept output after encoded ownership has been released.

This does not create a resident decode loop or background pump.

### Per-stream EOS drain

Accepted parser EOS moves the matching decoder stream through distinct states:

1. parser EOS observed;
2. null-packet flush accepted;
3. flush-accepted drain active;
4. decoder EOF observed.

After flush acceptance, one drain operation receives at most one frame. Frames may continue until EOF. `NeedMoreInput` during this drain is no progress and fails closed. Video and audio drain independently; full decoder completion requires EOF from both contexts.

Accepted flush alone must not be reported as fully drained output. Transport closure, empty PCP payload, cancellation, owner stop, timeout, and failure are not decoder EOF.

`avcodec_flush_buffers` remains excluded from EOS. Explicit discontinuity continues to replace the old generation; context reuse／reset is deferred.

### Bounds, cancellation, and failure

The future options must bound one-frame bytes below the existing 256 MiB hard copy limit and cumulative copied bytes／relief frames per outer operation. All counters use checked advancement.

Pre-native validation requires exact source, generation, native session, stream ID, media kind, codec role, stream state, and no active decoded completion.

Cancellation before commit advances no decoded sequence. Cancellation or failure after copy clears the frame unless ownership was transferred. Undefined results, contradictory state, source or context disposal, metadata／data rejection, allocation failure, consumer rejection, native rejection, relief no progress, flush-drain no progress, and budget exhaustion fail closed with fixed privacy-safe state.

### Cleanup

Required cleanup order is:

1. stop admission and cancel finite work;
2. dispose the active decoded completion and clear managed frame buffers;
3. dispose the submission state machine, releasing retained native packet before parser completion;
4. dispose the native session audio before video;
5. let the outer composition dispose parser／source ownership;
6. dispose factory ownership.

No PCP packet, callback payload, native frame pointer, scheduler, output sink, renderer, or device becomes owned by this boundary.

## Rejected alternatives

- State-only receive followed by a second receive for frame copying.
- Assuming one packet maps to one frame.
- Retrying encoded submission before decoded-frame ownership is settled.
- Retaining native frame pointers.
- Adding a second decoded-frame queue.
- Unbounded callback drain loops or resident tasks.
- Treating accepted flush or `NeedMoreInput` after flush as EOF.
- Using `avcodec_flush_buffers` for EOS.
- Reusing decoder contexts across discontinuity without a separate review.

## Required 0.0.9k validation

The implementation milestone must include focused cross-platform tests for:

1. exact source, generation, stream, media, and codec-role validation;
2. exactly one receive call per public operation;
3. one callback-scoped native frame becoming one owned managed frame without a second receive;
4. independent per-stream decoded sequence commit only after completion construction;
5. retained packet `EAGAIN` with a decoded frame accepted before one retry;
6. retained flush `EAGAIN` with the same ordering;
7. produced-frame backpressure blocking encoded retry and later PCP input;
8. relief `NeedMoreInput` failing as no progress;
9. ordinary post-accept `NeedMoreInput` completing normally;
10. multiple decoded frames requiring multiple finite operations;
11. video and audio flush-accepted drain producing frames before independent EOF;
12. flush-drain `NeedMoreInput` failing closed;
13. frame-count and managed-byte work limits;
14. cancellation before and after managed copy;
15. undefined／native／metadata／data／allocation failure cleanup;
16. discontinuity releasing active frame and pending submission before native-session replacement;
17. idempotent decoded-frame-first, submission-second, native-session-third cleanup.

## Deferred

- 0.0.9k runtime source-bound one-frame receive and EOS drain implementation;
- conversion and scheduler ingress for the real FLV path;
- production generation factory and ABI-gate acquisition;
- production backend activation and Windows lifecycle;
- discontinuity-time `avcodec_flush_buffers` policy;
- MP4／fragmented MP4 parsing;
- runtime bundling／distribution;
- controlled audible／visible playback.

## CI

CircleCI remains the routine Linux merge gate. The final documentation-only branch head must pass restore, Release build, the full test suite, and format verification.

Status: Complete when ADR-0120, this review, and the synchronized progress documents are merged from a final green head.