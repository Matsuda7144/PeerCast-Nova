# ADR-0121: Source-bound One-frame Receive and EOS Drain Boundary

## Status

Accepted for milestone 0.0.9k.

## Context

Milestone 0.0.9i connects awaited PCP packet memory through the bounded FLV parser to a source-bound packet／flush submission state machine. Milestone 0.0.9j confirmed that the existing production relief path observes frame progress through a state-only receive boundary that frees the successful temporary `AVFrame` before frame metadata or bytes can become managed ownership.

Calling a second receive boundary after that state-only result would consume a later decoder output rather than preserve the frame that created the progress. A source-bound generation therefore needs one receive authority that performs native receive, validation, managed copying, ownership transfer, and retained-submission coordination without a second receive or a decoded-frame queue.

## Decision

Add `PlaybackSourceBoundOneFrameDecodeBoundary` as one finite generation-owned boundary over an exact live FLV source session, its exact opened native decoder session, and a dedicated finite packet-submission state machine.

### Receive authority

Each public receive operation selects exactly one media kind and performs at most one callback-scoped native receive through the existing managed-frame copy boundary. A successful receive copies the native frame before the callback returns and produces one owned managed completion. No native frame pointer escapes and no second receive is used to recover frame data.

The boundary validates the exact source reference, decoder generation, stream ID, media kind, codec role, native-session binding, stream state, and active-completion state before native receive.

### Decoded completion

`PlaybackSourceBoundDecodedFrameCompletion` owns one `PlaybackDecoderFfmpegManagedFrame` and records:

- the exact source-session reference;
- decoder and discontinuity generations;
- media kind and exact stream ID;
- an independent checked per-stream decoded sequence;
- the managed byte count.

Only one decoded completion may remain active across the generation. The decoded sequence commits only after the completion is constructed and all applicable work budgets accept the copied bytes. Disposing the completion clears the managed frame before releasing the boundary admission gate.

### Receive modes

The boundary exposes three explicit finite modes:

- ordinary receive after accepted packet input;
- pending packet／flush relief;
- accepted-flush drain.

Ordinary `NeedMoreInput` is a normal finite result. `NeedMoreInput` during relief or flush drain is no progress and faults the generation fail closed.

### Retained submission relief

The generation uses a staging adapter around the existing finite submission state machine. A copied relief frame is exposed first and the retained packet／flush is not retried while that decoded completion remains active.

After the completion is released, a later finite relief call stages `FrameAvailable` into the submission state machine and permits at most one retained retry. Repeated `EAGAIN` therefore requires another separate one-frame receive and another separate one-retry operation. Decoder EOF is staged without an encoded retry and completes only the selected stream.

The production state-only receive path is not invoked by this boundary.

### EOS drain

Parser EOS, accepted null-packet flush, flush-drain activity, and decoder EOF remain distinct states. Once a stream flush is accepted, ordinary receive is rejected for that stream and caller-driven flush-drain operations receive at most one frame each until that decoder reports EOF.

Video and audio drain independently. Full decoded completion requires EOF from both contexts. `avcodec_flush_buffers` is not used for EOS.

### Bounds and terminal cleanup

One-frame managed bytes are limited below the existing 256 MiB hard copy limit. A reusable caller-owned budget may additionally bound cumulative frame count and managed bytes across an outer finite operation.

Cancellation before ownership transfer advances no decoded sequence. Cancellation after copy clears the copied frame. Undefined or contradictory results, invalid frame identity, native／metadata／data／allocation failures, no progress, and work-limit exhaustion fault the generation and release ownership in this order:

1. active decoded completion and managed frame;
2. pending packet／flush submission state;
3. native decoder session, audio before video.

The source session remains a non-owning identity reference.

## Consequences

- A successful native frame is preserved exactly once as managed ownership.
- Retained encoded submission cannot advance ahead of an unconsumed decoded frame.
- Multiple decoded frames and delayed EOS output require multiple explicit finite operations.
- The boundary adds no second decoded queue, resident loop, background task, production ABI-gate acquisition, scheduler delivery, output activation, or playback claim.
- Wiring this boundary into the 0.0.9i awaited composition and selecting decoded-completion delivery remain later reviewed work.
