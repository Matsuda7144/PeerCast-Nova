# PCN-0202: ISO BMFF Sample-count and Size-table Reconciliation

## Status

Implemented and verified as a non-physical composition slice.

## Implemented boundary

- required parsed `stts` timing input;
- exclusive `stsz` or `stz2` size-table selection;
- exact timing-to-size sample-count equality;
- matching empty-table support; and
- selected table kind, sample count, and total-byte projection.

## Verification

Managed tests cover matching standard and compact tables, exact total bytes,
matching empty tables, missing timing, missing size tables, conflicting size
tables, and count mismatches for both size forms.

No native runtime, output device, readiness capture, diagnostic, probe, or
physical audio/video path was exercised.

## Remaining work

Composition-time, dependency, sync-sample, partial-sync, chunk,
sample-description, and media-data reconciliation, lookup, and decoder
composition remain unimplemented.
