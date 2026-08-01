# PCN-0203: ISO BMFF Composition Sample-count Reconciliation

## Status

Implemented and verified as a non-physical composition slice.

## Implemented boundary

- optional parsed `ctts` input;
- exact present-`ctts` to reconciled sample-count equality;
- distinct composition-count mismatch; and
- successful composition-offset presence projection.

## Verification

Managed tests cover absent composition offsets, matching version 0 and version
1 tables, and a present composition-count mismatch.

No native runtime, output device, readiness capture, diagnostic, probe, or
physical audio/video path was exercised.

## Remaining work

Timestamp expansion, offset arithmetic, dependency, sync-sample, chunk,
sample-description, and media-data reconciliation, lookup, and decoder
composition remain unimplemented.
