# PCN-0209: ISO BMFF Sample-description Index Reconciliation

## Status

Implemented and verified as a non-physical description-index slice.

## Implemented boundary

- explicit parsed `stsd` input;
- required-table enforcement for explicit overloads;
- one-based `sample_description_index` upper-bound validation;
- validated description-count projection; and
- combination with expected sample-count agreement.

## Verification

Managed tests cover valid multiple description indexes, a missing explicit
table, an out-of-range index, empty description and chunk tables, and combined
sample-count validation. Existing chunk-table cases remain covered.

No native runtime, output device, readiness capture, diagnostic, probe, or
physical audio/video path was exercised.

## Remaining work

Sample-entry payload parsing, codec and decoder selection, data-reference
resolution, media-data bounds, lookup, and decoder composition remain
unimplemented.
