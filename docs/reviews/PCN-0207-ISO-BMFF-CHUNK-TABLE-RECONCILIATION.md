# PCN-0207: ISO BMFF Chunk-table Reconciliation

## Status

Implemented and verified as a non-physical chunk-table slice.

## Implemented boundary

- required parsed `stsc` input;
- exclusive parsed `stco` or `co64` selection;
- empty and nonempty table coverage agreement;
- final `first_chunk` upper-bound validation; and
- chunk count, run count, and offset-kind projection.

## Verification

Managed tests cover standard and large offsets, valid empty tables, missing
and conflicting tables, empty/nonempty coverage mismatches, and an out-of-range
run boundary.

No native runtime, output device, readiness capture, diagnostic, probe, or
physical audio/video path was exercised.

## Remaining work

Total sample-count calculation, per-chunk expansion, sample-description
validation, offset and media-data bounds, lookup, and decoder composition
remain unimplemented.
