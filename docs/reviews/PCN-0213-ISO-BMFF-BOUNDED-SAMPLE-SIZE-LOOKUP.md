# PCN-0213: Bounded ISO BMFF Sample-size Lookup

## Status

Implemented and verified as a non-physical sample-size lookup slice.

## Implemented boundary

- reconciled size-table kind enforcement;
- exclusive selected-table validation;
- exact sample-count and total-byte agreement;
- one-based sample-number bounds;
- unexpanded default `stsz` lookup; and
- individual `stsz` and `stz2` size projection.

## Verification

Managed tests cover default and individual standard sizes, compact sizes,
wrong and conflicting tables, count and total-byte mismatch, zero and
excessive sample numbers, and an empty table.

No native runtime, output device, readiness capture, diagnostic, probe, or
physical audio/video path was exercised.

## Remaining work

Chunk-local size accumulation, sample byte offsets and extents, media-data
bounds and reads, codec selection, and decoder composition remain
unimplemented.
