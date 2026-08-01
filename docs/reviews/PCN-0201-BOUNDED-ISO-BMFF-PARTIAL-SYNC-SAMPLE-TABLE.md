# PCN-0201: Bounded ISO BMFF Partial-sync Sample Table

## Status

Implemented and verified as a non-physical parser slice.

## Implemented boundary

- exact complete `stps` box validation;
- FullBox version 0 with zero flags;
- default 4,096-entry and hard 65,536-entry bounds;
- exact entry-count and payload-length agreement;
- nonzero, strictly increasing sample-number validation; and
- read-only unsigned 32-bit projection.

## Verification

Managed tests cover valid non-first and maximum sample numbers, an empty table,
extended and extends-to-end headers, both invalid limits, entry saturation,
short and extra entry bytes, zero, duplicate, and reversed sample numbers,
unsupported versions, nonzero flags, wrong type, declared-size shortage, and
trailing input.

No native runtime, output device, readiness capture, diagnostic, probe, or
physical audio/video path was exercised.

## Remaining work

Full-sync integration, sample-count reconciliation, seek and packet projection,
fragmented random-access metadata, media data, and decoder composition remain
unimplemented.
