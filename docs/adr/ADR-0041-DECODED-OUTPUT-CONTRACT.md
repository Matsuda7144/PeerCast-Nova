# ADR-0041: Decoded Output Contract Before Decoder Selection

## Status

Accepted for implementation candidate 0.0.5e.

## Context

The encoded-input lifecycle is verified, but selecting a real codec backend
before defining decoded output would let backend buffer rules determine Nova's
renderer and audio boundaries. Decoded audio/video memory can be much larger
than encoded packets, so ownership, format, and backpressure must be explicit.

## Decision

- Decoder creation receives an `IPlaybackDecodedFrameSink` from the runner.
- The initial canonical audio layouts are signed 16-bit and float 32-bit
  interleaved PCM.
- The initial canonical video layout is packed BGRA32 with explicit stride.
- Frame metadata is limited to sequence, timestamp, duration, discontinuity,
  fixed format enums, dimensions/rates, and payload memory.
- Payload memory is valid only until the awaited sink callback completes.
- A sink that needs retention copies before completing its callback.
- Decoder implementations await every sink callback, so sink delay is explicit
  backpressure and no unbounded decoder-owned output queue is implied.
- A null sink supports lifecycle diagnostics without claiming playback.
- No renderer, device, clock, decoded queue, real codec, or native dependency is
  selected by this milestone.

## Consequences

A future backend must convert its native output to one of Nova's canonical
layouts or add a separately reviewed fixed format. The first safe
implementation may copy at the sink boundary; zero-copy extensions require a
new ownership decision rather than implicit backend retention.
