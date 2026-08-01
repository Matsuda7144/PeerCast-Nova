# PCN-0195: Bounded ISO BMFF Sample-to-chunk Table

## Status

Implemented and verified as a non-physical parser slice.

## Implemented boundary

- exact complete `stsc` box validation;
- FullBox version 0 with zero flags;
- default 4,096-entry and hard 65,536-entry bounds;
- exact entry-count and payload-length agreement;
- one-based first entry and strictly increasing later `first_chunk` values;
- nonzero samples-per-chunk and sample-description indices; and
- read-only run projection without chunk or sample expansion.

## Verification

Managed tests cover valid and empty tables, maximum unsigned entry fields,
extended and extends-to-end headers, both invalid limits, entry saturation,
short and extra entry bytes, invalid first and subsequent chunk values, zero
samples-per-chunk and sample-description indices, unsupported versions, nonzero
flags, wrong type, declared-size shortage, and trailing input.

No native runtime, output device, readiness capture, diagnostic, probe, or
physical audio/video path was exercised.

## Remaining work

Chunk offsets, sample and chunk lookup, cross-table reconciliation, fragmented
runs, media data, and decoder composition remain unimplemented.
