# ADR-0060 Native Frame Metadata Extraction

## Status

Accepted for milestone 0.0.6k.

## Context

0.0.6j guarantees that a received `AVFrame` exists only during one internal
callback. Nova now needs bounded metadata before any later plane copy or format
conversion. FFmpeg 8.1 exposes the required values as public `AVFrame` fields;
it does not export field getter functions for format, dimensions, sample rate,
channel layout, sample count, timestamps, time base, or duration.

## Decision

- Read metadata only inside the existing 0.0.6j callback and retain only a
  fixed managed value after it returns.
- Pin the native reader to the official FFmpeg 8.1 `libavutil/frame.h` layout,
  libavutil ABI major 60, Windows x64 C alignment, and the already-verified
  five-module gate. The isolated reader contains named offsets derived from
  that public header; no inferred private field or data pointer is read.
- No additional symbol is added because FFmpeg 8.1 provides no official
  accessor for the selected fields. Existing allocation, receive, and free
  symbols remain mandatory through the symbol gate.
- Determine media kind from the context's fixed H.264 or AAC codec ID. Map only
  fixed native pixel/sample format values to managed enums.
- Use `frame.time_base` only when its numerator and denominator are valid.
  Convert known timestamps with checked decimal arithmetic; otherwise return
  fixed `PlaybackDecoderFfmpegFrameTime.Unknown` values without exposing native
  sentinels. Treat `duration == 0` as unknown. Reject negative duration,
  malformed time bases, conversion overflow, duration over ten seconds,
  undefined formats, and out-of-bound metadata before the consumer.
- Reuse the decoded output contract's dimension, sample-rate, channel-count,
  sample-count, and duration bounds.
- A consumer rejection preserves the context. An unexpected native/layout
  exception discards it fail closed. Non-Windows processes return one fixed
  unsupported result without native access.

## Consequences

The public result contains no pointer, native numeric error, frame bytes, path,
hash, or symbol. Changing the pinned FFmpeg ABI or architecture requires a new
review of every offset before activation. Plane/sample copying, conversion,
decoded frame creation, sink delivery, demux, queues, rendering, devices,
redistribution, and backend activation remain out of scope.
