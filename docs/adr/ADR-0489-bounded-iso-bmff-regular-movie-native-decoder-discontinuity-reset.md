# ADR-0489: Bounded ISO BMFF Regular Movie Native Decoder Discontinuity Reset

## Status

Accepted as the caller-quiesced decoder-context reset transaction required by
ADR-0488 discontinuity recovery.

## Decision

Borrow one available regular-movie native decoder session and reset exactly its
selected opened contexts through the existing verified
`avcodec_flush_buffers` capability boundary. Reset audio first and video
second, matching the cleanup ordering already used by timing and output reset.
An audio-only or video-only selection invokes only that selected role.

Acquire one zero-wait reset gate and honor cancellation before the first role
without changing the session. A complete reset increments one bounded count
and retains all context and session ownership. Any reset rejection before a
successful role faults and disposes the borrowed session because the native
context state cannot be proven reusable. Cancellation or rejection after one
role succeeded is a terminal `PartialResetFault` and also disposes the session.

The transaction itself does not own or dispose a healthy session. Its caller
must quiesce packet submission, managed-frame receive, scheduler delivery, and
all other context users for the complete reset call. This slice intentionally
does not claim that quiescence merely because its private reset gate is held.

## Consequences

Regular MP4 can now reuse already opened H.264 and AAC contexts across a
discontinuity without reopening parameters or codecs when every selected flush
succeeds. Ambiguous or partial native state is never retained.

## Non-goals

Top-level decode/delivery quiescence, packet cursor rewind, pending-packet
reconstruction, output/timing coordination, late-audio recovery, continuous
pumping, actual native invocation during verification, and physical playback
are not included.
