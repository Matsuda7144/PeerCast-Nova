# PCN-0212: Bounded ISO BMFF Sample-to-chunk Lookup

## Status

Implemented and verified as a non-physical sample-lookup slice.

## Implemented boundary

- reconciled run validation reuse;
- unsigned 64-bit one-based sample-number bounds;
- run-length sample-to-chunk mapping;
- chunk-local sample index and first-sample projection; and
- sample-description index projection.

## Verification

Managed tests cover first and later runs, chunk boundaries, zero and excessive
sample numbers, empty tables, invalid reconciliation, and maximum unsigned
32-bit samples-per-chunk arithmetic.

No native runtime, output device, readiness capture, diagnostic, probe, or
physical audio/video path was exercised.

## Remaining work

Sample-size and byte-offset lookup, chunk byte extents, media-data bounds and
reads, codec selection, and decoder composition remain unimplemented.
