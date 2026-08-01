# PCN-0210: Bounded ISO BMFF Chunk-run Lookup

## Status

Implemented and verified as a non-physical chunk-lookup slice.

## Implemented boundary

- reconciled `stsc` input validation;
- fail-closed chunk-number bounds;
- binary run lookup;
- inclusive run-range and mapping-value projection; and
- rejection of mismatched run, sample, or description counts.

## Verification

Managed tests cover first and later runs, zero and excessive chunk numbers,
empty tables, and mismatched reconciliation projections.

No native runtime, output device, readiness capture, diagnostic, probe, or
physical audio/video path was exercised.

## Remaining work

Per-sample lookup, byte-offset resolution, sample-size accumulation,
media-data bounds, codec selection, and decoder composition remain
unimplemented.
