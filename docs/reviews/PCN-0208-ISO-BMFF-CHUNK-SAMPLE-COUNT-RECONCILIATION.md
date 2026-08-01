# PCN-0208: ISO BMFF Chunk Sample-count Reconciliation

## Status

Implemented and verified as a non-physical chunk-count slice.

## Implemented boundary

- run-length `stsc` total sample-count calculation;
- unsigned 64-bit total projection;
- optional exact expected-count agreement; and
- distinct sample-count mismatch failure.

## Verification

Managed tests cover multiple runs, standard and large offsets, empty tables,
a matching expected count, and an expected-count mismatch. Existing missing,
conflicting, and coverage failures remain covered.

No native runtime, output device, readiness capture, diagnostic, probe, or
physical audio/video path was exercised.

## Remaining work

Per-chunk expansion, sample-description validation, offset and media-data
bounds, lookup, and decoder composition remain unimplemented.
