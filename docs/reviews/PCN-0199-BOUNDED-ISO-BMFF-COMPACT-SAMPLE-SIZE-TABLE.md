# PCN-0199: Bounded ISO BMFF Compact Sample-size Table

## Status

Implemented and verified as a non-physical parser slice.

## Implemented boundary

- exact complete `stz2` box validation;
- FullBox version 0, zero flags, and zero reserved fields;
- 4-, 8-, and 16-bit packed sample-size projection;
- default 4,096-entry and hard 65,536-entry bounds;
- exact packed payload-length validation;
- read-only unsigned 16-bit sizes and exact total-byte projection; and
- explicit odd 4-bit padding handling.

## Verification

Managed tests cover every field size, zero and maximum values, odd 4-bit
padding, empty tables, extended and extends-to-end headers, both invalid
limits, entry saturation, short and extra packed bytes, nonzero reserved
fields, unsupported field size and version, nonzero flags, wrong type,
declared-size shortage, and trailing input.

No native runtime, output device, readiness capture, diagnostic, probe, or
physical audio/video path was exercised.

## Remaining work

Sample-size table selection, cross-table reconciliation, lookup, fragmented
runs, media data, and decoder composition remain unimplemented.
