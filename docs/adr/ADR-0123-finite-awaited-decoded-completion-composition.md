# ADR-0123: Finite Awaited Decoded Completion Composition

## Status

Accepted for milestone 0.0.9m.

## Context

Milestone 0.0.9i introduced a serialized awaited PCP-to-FLV composition that owns a source-bound native decoder session and a separate finite packet-submission state machine. Milestone 0.0.9k then introduced `PlaybackSourceBoundOneFrameDecodeBoundary`, which must be the sole generation authority for packet submission, native receive, managed-frame copying, retained-send relief, and per-stream EOS drain.

ADR-0122 selected the ownership connection between those boundaries. The next implementation must preserve callback-scoped PCP payload lifetime, parser suffix accounting, active encoded completion ownership, one active decoded completion, and the finite one-frame／one-retry rule without introducing a decoded producer queue or resident loop.

The existing bounded scheduler remains the only future decoded queue. Its ownership-transfer adapter requires a separately reviewed one-shot detach contract and a generation-wide cross-media scheduler sequence, so scheduler transfer is not part of this milestone.

## Decision

Add `PlaybackPcpFlvAwaitedDecodeCompositionBoundary` as a new finite composition boundary. Keep the 0.0.9i boundary unchanged for compatibility and comparison until a later explicit cutover.

### Generation ownership

Each created `PlaybackPcpFlvAwaitedDecodeGeneration` owns:

1. one exact parser-created FLV source-session identity;
2. one `PlaybackSourceBoundOneFrameDecodeBoundary` with matching decoder and discontinuity generations;
3. one injected awaited decoded-completion settlement.

The decode boundary remains the sole owner of the opened native decoder session, finite packet-submission state, retained packet／flush state, decoded receive state, and active decoded completion. The composition does not keep a parallel native session or submission state machine.

Generation disposal releases settlement first and then the decode boundary. The source session remains non-owning from the generation perspective and is owned by the parser.

### PCP callback and parser progression

`ConsumeAsync` validates exact channel ID, channel-session ID, and strictly expected PCP packet sequence before parser work. It advances a local callback payload offset only by the parser's exact `ConsumedByteCount`.

The callback does not return until every byte is either consumed by a complete FLV record or defensively copied into the parser's bounded carry, and until no parser completion, retained submission, relief retry, or decoded completion remains active.

Multiple FLV events in one PCP callback remain serialized. A source-owned packet or per-stream EOS completion is fully submitted before parsing resumes at the same callback suffix.

### Awaited retained-submission relief

If the initial packet or null-flush submission returns `ReceiveRequired`, the same callback or explicit finalization operation drives relief through the 0.0.9k boundary.

One relief cycle performs exactly:

1. at most one native receive and managed copy;
2. one awaited settlement while the decoded completion remains active;
3. decoded-completion disposal after successful settlement;
4. one retry-only finite call for the retained packet or flush.

The retained encoded completion and callback payload lifetime remain blocked while settlement is incomplete. Repeated `EAGAIN` starts another explicit one-frame／one-settlement／one-retry cycle and is bounded by the shared per-call decode-operation, frame-count, and managed-byte budgets.

A relief frame is never silently discarded and the retained send is never retried before settlement and completion release.

### Settlement contract

`IPlaybackPcpFlvDecodedCompletionSettlement` receives the exact active `PlaybackSourceBoundDecodedFrameCompletion` and the caller cancellation token.

Before and after the awaited call, the composition validates exact source reference, decoder generation, discontinuity generation, media kind, stream ID, decoded sequence, managed-byte count, frame liveness, and frame media role.

0.0.9m accepts only `Consumed` as successful settlement. During the awaited call, the completion and managed frame remain owned by the decode boundary. On successful return the composition disposes the completion, releasing frame ownership and decode admission.

`Rejected`, `Faulted`, undefined results, contradictory completion state, and settlement exceptions fail the generation closed. `Cancelled` maps to the fixed cancellation path. A transferable or scheduler-owned settlement result is intentionally absent; one-shot frame detachment is deferred to a later reviewed milestone.

### Caller-driven decoded operations

Ordinary post-accept receive and accepted-flush drain are outside the PCP callback and explicit:

- `ReceiveDecodedAsync(mediaKind)` performs at most one ordinary receive and settlement;
- `DrainDecodedAsync(mediaKind)` performs at most one accepted-flush drain receive and settlement.

Ordinary `NeedMoreInput` is a normal finite result. Ordinary receive after a stream flush reports `StreamRequiresDrain`. Drain before flush reports `StreamNotFlushed`. Video and audio drain independently until each decoder reports EOF.

Encoded finalization and decoder drain completion remain distinct. `CompleteInputAsync` submits the parser's video-then-audio EOS sequence and enters `EncodedInputFinalized` after both stream flushes or EOF states are accepted. The composition reaches `DecodersDrained` only after both native decoder contexts report EOF.

### Bounds and serialization

One semaphore serializes PCP callbacks, explicit finalization, ordinary receive, and flush drain. There is no concurrent parser, submission, receive, or settlement operation.

Each public call has independent hard-bounded options for:

- parser operations;
- decode operations including retry-only transitions;
- decoded frame count;
- aggregate managed decoded bytes.

The same reusable decode budget covers every relief frame within one callback or finalization call.

### Cancellation, discontinuity, and failure

Caller cancellation, stop cancellation, subordinate fixed `Cancelled` results, and settlement cancellation are translated only after active ownership is released and terminal state is latched. Because cancellation may originate from a subordinate enum or linked stop source rather than only the caller token, CA2250 is disabled for this file with a documented file-scoped rationale.

A PCP discontinuity disposes the old settlement and decode generation before applying parser discontinuity and creating a new source-bound generation. Decoder-generation numbers increase independently from the parser discontinuity generation and are checked exactly.

Invalid identity, sequence mismatch, parser accounting contradiction, generation mismatch, rejected submission, no progress, budget exhaustion, settlement rejection, undefined states, and unexpected integration failures fault the composition with a fixed public exception message.

Terminal cleanup order is:

1. active decoded completion and managed frame through decode-boundary disposal;
2. retained packet／flush submission state;
3. native decoder session, audio before video;
4. parser source session and parser state;
5. generation factory.

The generation wrapper initiates settlement disposal before decode-boundary cleanup.

## Consequences

- Callback-scoped PCP memory cannot outlive `ConsumeAsync` and cannot be resumed ahead of unsettled decoded output.
- The successful native frame that creates relief progress is preserved and settled exactly once before retry.
- Ordinary receive and EOS drain remain finite caller-driven operations.
- Encoded finalization is not misreported as native decoder EOF or downstream delivery completion.
- No second encoded queue, decoded queue, resident loop, background pump, scheduler transfer, production ABI-gate acquisition, backend activation, Windows output lifecycle, FFmpeg distribution, or audible／visible playback claim is added.
- The next milestone reviews one-shot decoded-frame transfer, generation-wide scheduler sequence assignment, and bounded scheduler ingress without weakening this awaited-consumption contract.
