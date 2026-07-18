# ADR-0063 Decoded Output Adapter

## Status

Accepted for milestone 0.0.6n.

## Decision

- Revalidate the complete owned 0.0.6m metadata and plane contract before
  constructing existing `PlaybackDecodedVideoFrame` or
  `PlaybackDecodedAudioFrame` values.
- Adapt padding-free BGRA32 using its logical row bytes as output stride. Adapt
  packed S16/F32 directly to the existing interleaved PCM formats.
- Recognize and validate YUV420P, NV12, planar S16, and planar F32, but return
  fixed `ConversionRequired` because existing output models cannot represent
  them without a format conversion, which remains out of scope.
- Map unknown timestamps to `TimeSpan.Zero` and unknown duration to one tick,
  the smallest duration accepted by the existing output contract.
- Output payload is temporary and callback-scoped. It is cleared immediately
  after the consumer returns, whether accepted or rejected. The input managed
  frame remains caller-owned and must be disposed by its owner.
- Reject disposed frames, undefined formats, inconsistent plane contracts,
  overflow, and totals above 256 MiB with fixed results. Consumer rejection
  preserves the decoder context.

## Consequences

The adapter adds no pixel/sample conversion, sink delivery, queue, renderer,
audio device, demux, A/V synchronization, or backend activation.
