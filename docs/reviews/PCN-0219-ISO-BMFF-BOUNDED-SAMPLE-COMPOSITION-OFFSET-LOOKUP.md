# PCN-0219: Bounded ISO BMFF Sample Composition-offset Lookup

## Status

Implemented and verified as a non-physical timing-lookup slice.

## Implemented boundary

- optional-table presence reconciliation;
- one-based sample and table-count validation;
- version-correct run offset selection; and
- explicit versus implicit-zero projection without per-sample expansion.

## Verification

Managed tests cover version 0 unsigned and version 1 negative offsets,
multi-run selection, absent-table zero, a maximum-size uniform run, both
sample-number bounds, presence mismatches, count mismatch, and an empty
reconciliation.

No native runtime, payload read, output device, readiness capture, diagnostic,
probe, or physical audio/video path was exercised.

## Remaining work

Decode-time addition, presentation timestamps, timescale conversion, edit
lists, packet projection, and decoder composition remain unimplemented.
