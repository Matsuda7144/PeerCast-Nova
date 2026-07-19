# ADR-0099 Diagnostic Awaited Sink Delivery Transaction

## Status

Accepted for milestone 0.0.8o.

## Decision

Add one diagnostic-only asynchronous transaction with this fixed order:

1. send one H.264 packet;
2. copy at most one video frame;
3. await one video sink delivery when a frame is available;
4. send one AAC packet;
5. copy at most one audio frame;
6. await one audio sink delivery when a frame is available.

The transaction uses sequence `0` for video, sequence `1` for audio, and the
`None` discontinuity value. It introduces no loop, queue, resident task, retry,
or timing policy.

The 0.0.8n completion cannot be used as the input to delivery because converted
payload is intentionally valid only during its conversion callback. 0.0.8o
therefore passes each owned copied frame directly to the existing 0.0.6p awaited
delivery boundary. That boundary performs direct adaptation or managed format
conversion and keeps payload valid until the awaited sink callback completes.

`Copied` is accepted only with one live frame of the expected media kind.
`NeedMoreInput` is accepted only without a frame and skips delivery for that
stream. Every copied frame is disposed immediately after its awaited delivery
returns. Neither the copied frame nor decoded payload survives transaction
return.

The sink is caller-owned and is never disposed. Successful completion retains
only the existing aggregate native decoder lease and fixed copy/delivery states.
The completion owns that lease until disposal.

Cancellation, rejected delivery, contradictory state, or unexpected failure
ends this finite diagnostic attempt and disposes the complete aggregate lease.
This transaction-level fail-closed policy is stricter than the lower delivery
boundary, which may preserve an individual context after cancellation or sink
failure.

## Non-goals

No decoded-frame queue, scheduler, renderer or audio-device activation, packet
loop, flush, EOS handling, demux, production decoder activation, FFmpeg
redistribution, or approval-state change is added.
