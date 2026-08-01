# PCN-0214: Bounded ISO BMFF Sample Byte-range Lookup

## Status

Implemented and verified as a non-physical byte-range slice.

## Implemented boundary

- cross-reconciliation sample-count agreement;
- composed chunk, offset, and size validation;
- unexpanded uniform-size offset calculation;
- bounded individual-size accumulation;
- absolute offset and exclusive-end projection; and
- prefix and end overflow rejection.

## Verification

Managed tests cover uniform, individual, and compact sizes, multiple chunks,
sample bounds, reconciliation mismatch, empty tables, and both offset-overflow
locations.

No native runtime, output device, readiness capture, diagnostic, probe, or
physical audio/video path was exercised.

## Remaining work

File-length and media-data bounds, byte reads, fragmented runs, codec
selection, and decoder composition remain unimplemented.
