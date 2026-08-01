# PCN-0200: Bounded ISO BMFF Sample-dependency Type Table

## Status

Implemented and verified as a non-physical parser slice.

## Implemented boundary

- exact complete ISO-form `sdtp` box validation;
- FullBox version 0 with zero flags;
- payload-derived entry count with default 4,096 and hard 65,536 bounds;
- zero reserved-bit and non-reserved two-bit value validation; and
- distinct typed, read-only dependency-field projection.

## Verification

Managed tests cover every accepted field value, empty tables, extended and
extends-to-end headers, both invalid limits, entry saturation, every reserved
upper-bit form, reserved values in each dependency field, unsupported version,
nonzero flags, wrong type, declared-size shortage, and trailing input.

No native runtime, output device, readiness capture, diagnostic, probe, or
physical audio/video path was exercised.

## Remaining work

Sample-count reconciliation, keyframe inference, packet projection, seek
policy, fragmented sample flags, media data, and decoder composition remain
unimplemented.
