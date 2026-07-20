# ADR-0120: Bounded Decode Iteration and Per-stream EOS Drain

Status: Accepted

## Context

0.0.9i connects the awaited PCP packet callback to the one-event FLV parser, one exact source-bound native decoder generation, and the finite packet-submission state machine. Encoded payload ownership, parser completion ownership, packet／flush `EAGAIN`, explicit final input, and discontinuity generation replacement are now finite and source-bound.

Decoded output is not yet preserved by that production path. `PlaybackFiniteOneEventPacketSubmissionStateMachine.Advance` currently asks its production operations for `PlaybackDecoderFfmpegFrameReceiveResult`. Those operations call the state-only `PlaybackDecoderFfmpegPacketFrameBoundary.ReceiveFrame`, which allocates one temporary `AVFrame`, calls `avcodec_receive_frame`, maps only the native state, and frees the frame before returning. A later second receive cannot recover the same decoded frame.

The repository already has a separate callback-scoped frame path. `PlaybackDecoderFfmpegNativeFrameBoundary` keeps one native frame valid only inside one callback. Metadata and plane／sample boundaries validate that frame, and `PlaybackDecoderFfmpegManagedFrameCopyBoundary` can create one independently owned managed frame before the native frame is freed. Those boundaries are diagnostic and are not source-bound to the 0.0.9i generation.

Null-packet flush acceptance is also not decoder completion. The 0.0.9e state machine records independent video／audio flush acceptance, but no owner currently continues receiving each context until decoder EOF. Parser EOS, accepted decoder flush, and decoder EOF are distinct states.

## Decision

Select one future source-bound one-frame receive and drain boundary for 0.0.9k. It will be owned by the exact 0.0.9i decoder generation and will become the sole production receive authority for that generation's video and audio contexts.

### One native receive, one observable result

One finite receive operation performs at most one `avcodec_receive_frame` call against exactly one selected stream context.

It must use the callback-scoped native frame path and the existing managed frame copy boundary as one continuous operation. A successful native receive therefore returns ownership of exactly one copied `PlaybackDecoderFfmpegManagedFrame`; it must not first call the state-only receive boundary and then attempt a second receive for copying.

No native frame pointer, plane pointer, line-size array, ABI offset, or raw native result escapes the callback. The native frame is freed before the operation returns. The copied managed frame is the only frame owner that may survive the callback.

### Source-bound decoded completion

A successful copied frame is wrapped in one source-bound decoded completion containing fixed data only:

- the exact non-owning `PlaybackBoundedDemuxSession` identity;
- the parser／decoder generation number;
- exact media kind and stream ID;
- one independent monotonic decoded-frame sequence for that stream;
- the owned managed frame and its existing validated metadata;
- the current discontinuity generation marker.

Decoded-frame sequence is not copied from encoded packet sequence. Decoder reordering and one-packet-to-many-frame behavior make that mapping invalid. Sequence advances only after a decoded completion is constructed successfully.

The managed metadata keeps both presentation and best-effort timestamps. This milestone does not synthesize a timestamp, copy an encoded timestamp onto a frame, or choose a playback clock. A later output adapter may prefer presentation timestamp, then best-effort timestamp, while preserving Unknown when neither is known.

Only one decoded completion may be active across the generation. Until it is consumed or disposed, no further native receive, retained packet／flush retry, parser input, or new PCP packet is admitted.

### Pending-send relief

When packet or flush submission returns `ReceiveRequired`, the same awaited PCP／finalization operation remains open because the encoded submission and parser completion are still active.

One relief transition may:

1. receive at most one frame from the same media context;
2. construct and transfer one source-bound decoded completion;
3. retry the retained packet or flush at most once, and only after the decoded completion has been accepted and no decoded ownership remains active.

If a frame is produced but downstream does not accept ownership, the frame and encoded submission remain pending. The retained send／flush is not retried and the callback does not complete successfully.

`NeedMoreInput` during pending-send relief is explicit no progress and remains terminal for the 0.0.9i composition. Decoder EOF completes that stream and releases the retained submission through the existing fail-closed state transition.

Every callback／finalization call has independent limits for relief-frame count, cumulative copied bytes, and submission advances beneath hard project limits. Exceeding a limit is terminal; no sleep, spin, background drain, or hidden retry pump is added.

### Ordinary post-accept receive

Accepted packet submission does not imply zero or one output frame. One packet may produce no frame, one frame, or multiple frames, and frames may be reordered.

After packet acceptance, later caller-driven finite receive operations may select that stream and receive at most one frame per call. A copied frame transfers one active decoded completion. `NeedMoreInput` is a normal finite boundary indicating that no additional frame is currently available before more encoded input. Decoder EOF marks that stream complete and rejects later encoded input for it.

The awaited PCP callback is not kept open merely to exhaust all currently available post-accept output. Once its encoded bytes are consumed, its parser completion is released, no retained submission exists, and its configured relief work is complete, it may return. Post-accept receive remains an explicitly driven finite operation owned by the higher composition layer.

### Accepted flush and independent EOS drain

Parser per-stream EOS, accepted null-packet flush, and decoder EOF remain separate.

After one stream's flush is accepted:

- later packet input for that stream remains rejected;
- the stream enters `FlushAcceptedDraining`;
- caller-driven drain operations receive at most one frame from that stream per call;
- each copied frame must be consumed before another receive;
- decoder EOF moves only that stream to `EndOfStream`;
- the other stream continues independently.

`NeedMoreInput` after an accepted flush is treated as drain no progress and fails closed rather than being mistaken for completion. A decoder generation is fully drained only after both video and audio reach decoder EOF. `PlaybackPcpFlvCompositionBoundary.CompleteInputAsync` remains encoded-input finalization; a later decoded-output completion state must not claim full completion merely because both flushes were accepted.

`avcodec_flush_buffers` is not EOS drain. It remains outside this milestone and may be reviewed separately for seek／discontinuity reuse. Current explicit discontinuity replaces the old decoder generation rather than resetting and reusing its contexts.

### Bounds and failure

The future boundary must validate the exact live source session, native decoder session, media kind, stream ID, generation, context codec role, stream drain state, and absence of another active decoded completion before native access.

Options must bound:

- one native receive per public operation;
- relief frames and decoder advances per PCP／finalization call;
- maximum copied bytes for one frame beneath the existing 256 MiB hard copy limit;
- cumulative copied bytes per PCP／finalization call;
- decoded sequence magnitude and checked counter advancement.

Cancellation before ownership transfer performs no successful commit. Cancellation or failure after a managed frame is copied disposes that frame unless ownership was explicitly accepted. Undefined enums, contradictory states, source disposal, context disposal, metadata／plane validation failure, allocation failure, consumer rejection, native rejection, no progress in relief or flush-drain mode, and work-limit exhaustion fail closed with fixed privacy-safe results.

### Ownership and cleanup

The future generation cleanup order is:

1. close admission and cancel active finite work;
2. dispose the active source-bound decoded completion and clear its managed frame buffers;
3. dispose the packet-submission state machine so retained native packet ownership is released before its parser completion;
4. dispose the native decoder session, preserving audio-before-video context cleanup;
5. allow the outer composition to dispose the parser facade and source session;
6. release generation-factory ownership.

The decode boundary does not own the PCP session, PCP packet, callback payload, parser source session, scheduler, renderer, audio device, or output activation.

## Rejected alternatives

- Calling the state-only receive boundary and then receiving again to copy the frame.
- Assuming one accepted encoded packet produces exactly one decoded frame.
- Retrying a retained packet／flush before a produced frame has been accepted.
- Holding a native `AVFrame` pointer outside its callback.
- Adding a decoded-frame queue beside the existing bounded scheduler.
- Draining every available frame in an unbounded loop inside the PCP callback.
- Treating accepted flush as decoder EOF.
- Treating `NeedMoreInput` during accepted-flush drain as successful completion.
- Using `avcodec_flush_buffers` as EOS signaling.
- Reusing old decoder contexts across explicit discontinuity before a separate reset review.

## Consequences

0.0.9j selects a single-receive, source-bound managed-frame ownership model. It preserves every decoded frame observed while relieving packet backpressure, separates ordinary output availability from EOS drain, and keeps video and audio decoder completion independent.

The next milestone is 0.0.9k Source-bound One-frame Receive and EOS Drain Boundary. It will implement the reviewed result／ownership contract with injected cross-platform receive operations and focused tests. Production generation-factory activation, scheduler ingress, conversion／delivery composition, Windows output activation, runtime distribution, and audible／visible playback remain unchanged.