# PCN-0211: Bounded ISO BMFF Chunk-offset Lookup

## Status

Implemented and verified as a non-physical offset-lookup slice.

## Implemented boundary

- reconciled offset-kind enforcement;
- exclusive selected-table validation;
- exact offset entry-count agreement;
- one-based chunk-number bounds; and
- unified unsigned 64-bit offset projection.

## Verification

Managed tests cover standard and large offsets, maximum-width values, wrong or
conflicting tables, count mismatch, zero and excessive chunk numbers, and an
empty table.

No native runtime, output device, readiness capture, diagnostic, probe, or
physical audio/video path was exercised.

## Remaining work

File-length and media-data bounds, byte reads, per-sample offset calculation,
codec selection, and decoder composition remain unimplemented.
