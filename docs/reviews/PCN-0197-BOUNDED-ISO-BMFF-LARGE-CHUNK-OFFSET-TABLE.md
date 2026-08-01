# PCN-0197: Bounded ISO BMFF Large Chunk-offset Table

## Status

Implemented and verified as a non-physical parser slice.

## Implemented boundary

- exact complete `co64` box validation;
- FullBox version 0 with zero flags;
- default 4,096-entry and hard 65,536-entry bounds;
- exact entry-count and payload-length agreement;
- unsigned 64-bit offset preservation without premature range or ordering
  policy; and
- read-only offset projection.

## Verification

Managed tests cover values above the 32-bit range, zero, non-monotonic,
maximum, and empty offsets, extended and extends-to-end headers, both invalid
limits, entry saturation, short and extra entry bytes, unsupported versions,
nonzero flags, wrong type, declared-size shortage, and trailing input.

No native runtime, output device, readiness capture, diagnostic, probe, or
physical audio/video path was exercised.

## Remaining work

Chunk-offset table selection, media-data range validation, sample-to-chunk
reconciliation, lookup, fragmented runs, media data, and decoder composition
remain unimplemented.
