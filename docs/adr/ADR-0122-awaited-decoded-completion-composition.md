# ADR-0122: Awaited Decoded Completion Composition

## Status

Accepted for milestone 0.0.9l.

## Context

Milestone 0.0.9i owns the awaited PCP packet callback, the one-event FLV parser facade, one parser-created source session, and a generation that currently contains an opened native decoder session plus a finite packet-submission state machine. When packet or flush submission returns `ReceiveRequired`, that composition calls the submission state machine's state-only `Advance` operation until the retained submission is accepted, reaches decoder EOF, or fails.

Milestone 0.0.9k replaces that unsafe receive model with `PlaybackSourceBoundOneFrameDecodeBoundary`. The boundary owns the exact native session and a dedicated submission state machine, performs one native receive and managed copy per operation, exposes one source-bound decoded completion, delays retained packet or flush retry until that completion is released, and drains video and audio independently after accepted flush.

The two boundaries are not yet composed. The current 0.0.9i generation must not keep its separate submission state machine while also adding the 0.0.9k boundary, because that would create two submission owners and two receive authorities for the same decoder contexts. Decoded completion ownership must also be settled before the awaited PCP callback resumes parser suffix input.

Existing downstream assets provide three useful constraints:

- `PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary` serially awaits one decoded sink callback, owns no queue, and keeps temporary converted payload valid only through that callback;
- `BoundedPlaybackScheduler` is the existing sole bounded decoded-frame queue and takes ownership only for accepted enqueue results;
- the scheduler uses one generation-wide monotonic sequence across both media kinds, while 0.0.9k intentionally exposes independent video and audio decoded sequences.

## Decision

Select one future awaited composition in which the 0.0.9i generation owns exactly one 0.0.9k decode boundary and an injected decoded-completion settlement boundary. The decode boundary is the sole packet-submission and native-receive authority for that generation.

### Generation ownership

A successful future generation contains:

1. the exact non-owning parser-created `PlaybackBoundedDemuxSession` identity;
2. one `PlaybackSourceBoundOneFrameDecodeBoundary`, which owns the opened native decoder session and its dedicated finite submission state machine;
3. one generation-scoped decoded-completion settlement boundary;
4. fixed decoder and discontinuity generation identifiers;
5. bounded counters and one generation-wide downstream-admission sequence.

The parser facade remains owned by the outer PCP composition. A later production generation factory may temporarily own a decoder-parameter binding and distinct ABI gate leases while constructing the native session. On success, those temporary owners are consumed into the opened session and then into the decode boundary; they must not remain as parallel successful-generation owners.

The factory object remains owned by the outer composition. No generation owns the PCP session, PCP callback payload, parser facade, output device, renderer, or application scheduler loop.

### Awaited decoded-completion settlement

The future composition uses one injected asynchronous settlement contract for an exact active `PlaybackSourceBoundDecodedFrameCompletion`. The contract validates source reference, decoder generation, discontinuity generation, media kind, stream ID, stream-local decoded sequence, managed-byte count, and undisposed frame ownership before any downstream work.

An incomplete `ValueTask` is the backpressure signal. While settlement is incomplete:

- the decoded completion remains the only managed-frame owner;
- a retained encoded packet or flush remains active;
- the PCP callback or explicit-finalization operation remains open when relief caused the frame;
- parser suffix input, another parser completion, another native receive, another PCP packet, and retained-send retry remain blocked.

A completed settlement has one fixed outcome:

- **Consumed**: an awaited callback used or copied the frame and the composition may dispose the managed completion;
- **Transferred**: ownership was detached exactly once into an approved downstream owner such as the existing scheduler;
- **Rejected**, **Cancelled**, or **Faulted**: the generation fails closed and no retained encoded retry is permitted.

There is no returned “still retained” result. Continued retention is represented only by the asynchronous operation remaining incomplete. Success commits only after downstream settlement and decoded-completion release.

### Retained packet and flush relief

When `BeginSubmission` returns `ReceiveRequired`, the same awaited PCP callback or `CompleteInputAsync` operation remains open.

One bounded relief cycle performs:

1. one `PendingSubmissionRelief` receive from the exact pending media context;
2. at most one decoded completion construction;
3. one awaited settlement of that completion;
4. decoded-completion release after successful settlement;
5. one later finite relief call that performs no second native receive and retries the retained packet or flush at most once.

If the retry returns another `ReceiveRequired`, another complete relief cycle is required. The outer operation may repeat only under shared frame-count, managed-byte, decode-advance, and parser-operation limits. `NeedMoreInput` during relief remains terminal no progress. Decoder EOF settles only the selected stream and performs no encoded retry.

The PCP callback may not return successfully while callback payload, parser completion, retained native packet or flush, active decoded completion, or a relief retry remains required.

### Ordinary receive outside the PCP callback

Accepted packet input does not require the PCP callback to exhaust every available decoded frame. After the callback has consumed all encoded bytes and no retained submission remains, it may return.

The outer owner later drives one serialized ordinary decoded-output operation. Each call:

- selects exactly one media kind and exact stream ID;
- performs at most one native receive;
- settles at most one decoded completion;
- treats `NeedMoreInput` as a normal finite result;
- returns without a hidden loop or background pump.

The ordinary operation shares the same generation admission gate as PCP consumption, explicit finalization, relief, and flush drain. No operation may overlap another operation for the same generation.

### Explicit finalization and independent drain

Encoded-input finalization and decoded-output drain remain distinct.

`CompleteInputAsync` may signal final parser input, submit video and audio EOS one event at a time, and perform bounded relief required to get each null flush accepted. It must not claim complete decoded output merely because both flushes were accepted.

After flush acceptance, caller-driven finite drain operations select video or audio and perform at most one receive and one settlement per call. Video and audio reach decoder EOF independently. `NeedMoreInput` during flush drain remains terminal no progress.

The composition distinguishes at least:

- accepting encoded input;
- encoded input finalized with decoded drain pending;
- both decoders at EOF with scheduler or sink ownership possibly still outstanding;
- terminal failure;
- disposed.

Full user-visible playback completion is outside this milestone and cannot be inferred from decoder EOF while downstream scheduled frames remain owned.

### Existing scheduler reuse

The future real-FLV ingress reuses `BoundedPlaybackScheduler` as the only decoded queue. It does not add a queue inside the PCP composition, decode boundary, settlement boundary, or sink adapter.

A source-bound decoded completion has an independent per-stream sequence. That value remains part of source identity and validation but is not directly used as the scheduler's cross-media sequence. The generation maintains a separate checked downstream-admission sequence:

- the next candidate is passed to scheduler ingress;
- it commits only when ownership is accepted;
- video and audio therefore share one strictly increasing scheduler sequence;
- overflow fails closed;
- a new discontinuity generation starts with an explicit decoder-reset marker rather than silently reusing stale scheduler state.

Only `Enqueued` and `EnqueuedAfterDroppingOldest` are accepted ownership transfers under the currently reviewed ingress policy. Other enqueue results are terminal for the composition. The exact production full-policy choice remains a later activation decision.

Because queued managed FFmpeg frames require the matching decoder context for later conversion and delivery, one scheduler ownership generation must not outlive or cross into a replacement native decoder generation. Explicit discontinuity disposes old scheduler-owned frames before disposing the old decode boundary and native session. A fresh generation receives a fresh downstream sequence and reset marker.

### Direct awaited sink delivery

`PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary` remains an approved no-queue adapter for later output composition. If used as a settlement implementation, it must use the exact context owned by the same decode generation without exposing that context publicly. The input managed frame remains owned by the decoded completion until the awaited callback finishes; temporary converted payload remains callback-scoped and is cleared before settlement returns.

The first implementation after this review may use an injected fake settlement boundary and defer real scheduler or sink activation. It must not introduce both direct sink delivery and scheduler ownership for the same frame.

### Ownership transfer requirements

The current decoded completion exposes its frame but has no detach operation. A later scheduler adapter therefore requires one generation-controlled, one-shot ownership transfer primitive. The primitive must:

- verify that the completion is still the exact active completion;
- detach the managed frame at most once;
- prevent later completion disposal from clearing scheduler-owned buffers;
- release the generation's active-completion gate only after downstream ownership has settled;
- fail closed if transfer and downstream result contradict each other.

If a downstream API consumes or disposes a detached frame while returning rejection, the composition treats that outcome as terminal and cleans the retained submission; it does not reconstruct or retry the lost frame.

### Bounds and cancellation

Each PCP callback, explicit-finalization call, ordinary receive call, and drain call owns explicit finite budgets. Relief uses a shared budget across all repeated frames and retries within the outer call. Scheduler capacity and aggregate owned-byte limits remain independent downstream bounds.

Cancellation before decoded ownership settlement commits no downstream-admission sequence and permits no retained retry. Cancellation after an accepted scheduler transfer does not reclaim scheduler-owned memory; stop or failure cleanup disposes the generation scheduler. Unexpected exceptions, undefined results, source disposal, generation mismatch, contradictory completion state, consumer rejection, sequence overflow, budget exhaustion, and delivery integration failure terminate the generation with fixed privacy-safe results.

### Discontinuity and cleanup

Explicit discontinuity first closes admission for the old generation. Cleanup order is:

1. cancel or finish the active settlement operation;
2. dispose the active decoded completion and clear its managed frame when ownership was not transferred;
3. dispose generation-owned scheduler frames or another transferred downstream owner;
4. dispose the 0.0.9k decode boundary, which releases retained packet before parser completion and then disposes native audio before native video;
5. reset the parser facade and create the new parser source generation;
6. release generation-factory temporary ownership.

Terminal outer composition cleanup follows the same relative order, then disposes the parser facade and source session before the factory object.

## Rejected alternatives

- Keeping the 0.0.9i submission state machine beside the 0.0.9k decode boundary.
- Calling the old state-only `Advance` path for relief after introducing managed decoded completions.
- Returning the PCP callback while a relief frame or retained encoded submission is unsettled.
- Draining all post-accept output inside every PCP callback.
- Retrying a packet or flush before downstream settlement releases the decoded completion.
- Passing independent video or audio decoded sequence directly into the scheduler's single cross-media sequence.
- Letting scheduler-owned old-generation frames survive native-session replacement.
- Copying the managed frame into a second composition queue.
- Delivering the same decoded frame both directly to a sink and through the scheduler.
- Treating accepted flush, decoder EOF, or scheduler enqueue as complete user-visible playback.

## Consequences

The design preserves callback payload lifetime during required decoder relief, gives every decoded frame one explicit settlement point, and keeps ordinary receive and EOS drain finite and caller-driven. The existing bounded scheduler remains the only permitted decoded queue, with a separate generation-wide admission sequence and generation-matched context lifetime.

Milestone 0.0.9l changes documentation only. The next milestone is 0.0.9m Finite Awaited Decoded Completion Composition Boundary, which will replace the 0.0.9i generation internals with the 0.0.9k boundary and an injected awaited settlement seam under cross-platform focused tests. Real scheduler ingress, production generation-factory ABI-gate acquisition, backend activation, output lifecycle, FFmpeg distribution, and audible or visible playback remain later work.