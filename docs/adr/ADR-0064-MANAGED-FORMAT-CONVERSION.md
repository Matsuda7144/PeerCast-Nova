# ADR-0064 Managed Format Conversion

## Status

Accepted for milestone 0.0.6o.

## Decision

- Convert only padding-free, owned 0.0.6m buffers after independently
  revalidating metadata, plane count/order, rows, logical row bytes, and exact
  buffer lengths.
- Convert YUV420P and NV12 to packed BGRA32 with fixed BT.601 limited-range
  integer coefficients. For `C = Y - 16`, `D = U - 128`, `E = V - 128`:
  `R = (298C + 409E + 128) >> 8`,
  `G = (298C - 100D - 208E + 128) >> 8`, and
  `B = (298C + 516D + 128) >> 8`.
  Clamp every channel to 0–255 and set alpha to 255. No colorspace or range
  auto-detection is performed.
- Address 4:2:0 chroma with `x / 2` and `y / 2`; chroma dimensions use ceiling
  division so odd width and height remain in bounds.
- Interleave planar S16 and F32 in sample, channel, then byte order. Sample
  bytes are preserved exactly; no numeric sample conversion occurs.
- Calculate input and output sizes with checked arithmetic, reuse existing
  decoded-frame bounds, and reject either side above 256 MiB.
- The input frame remains caller-owned. Converted payload is valid only during
  the existing decoded-output callback and is cleared after success, rejection,
  consumer exception, conversion failure, or output-contract failure.
- Reuse 0.0.6n Unknown timestamp (`TimeSpan.Zero`) and duration (one tick)
  mappings. Consumer rejection preserves decoder context state.

## Consequences

This is a scalar managed baseline. SIMD, GPU conversion, swscale, swresample,
sink delivery, queueing, rendering, audio devices, demux, A/V synchronization,
and backend activation remain out of scope.
