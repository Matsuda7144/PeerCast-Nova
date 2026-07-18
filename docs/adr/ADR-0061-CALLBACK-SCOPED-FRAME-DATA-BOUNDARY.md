# ADR-0061 Callback-Scoped Frame Data Boundary

## Status

Accepted for milestone 0.0.6l.

## Context

0.0.6j confines each received `AVFrame` to one callback, and 0.0.6k converts
its metadata to bounded managed values. The next boundary must validate the
corresponding video planes or audio sample planes without extending either the
frame pointer or data-pointer lifetime.

FFmpeg 8.1 has no public accessor functions for `AVFrame.data`, `extended_data`,
or `linesize`. They are public fields in `libavutil/frame.h`.

## Decision

- Use only metadata produced inside the same 0.0.6j callback. Read data pointers
  and line sizes only after that metadata has passed the 0.0.6k validation.
- Pin the isolated reader to the official FFmpeg 8.1 libavutil 60 Windows x64
  public layout: `data` offset 0, `linesize` offset 64, and `extended_data`
  offset 96. Any ABI or architecture change requires a new offset review.
- Expose each accepted plane to the consumer as a callback-only `ref struct`.
  It contains bounded dimensions and byte access but does not expose a pointer
  and cannot be retained by ordinary managed objects.
- Accept only YUV420P, NV12, BGRA32, packed S16/F32, and planar S16/F32 layouts
  that match the managed metadata. Reject null pointers, non-positive or short
  line sizes, values above existing decoded-frame limits, checked-arithmetic
  overflow, and more than 256 MiB of referenced data per frame.
- Validate every descriptor before invoking the consumer. A consumer rejection
  preserves the context; unexpected native or layout exceptions discard it.
- On non-Windows or non-x64 processes, return one fixed unsupported result
  without reading native memory.

## Consequences

No AVFrame pointer, plane pointer, path, hash, native error, or frame content is
returned in a result or log. This milestone does not copy data into managed
buffers and does not perform pixel/sample conversion, decoded-frame creation,
sink delivery, demux, queuing, rendering, or audio output.
