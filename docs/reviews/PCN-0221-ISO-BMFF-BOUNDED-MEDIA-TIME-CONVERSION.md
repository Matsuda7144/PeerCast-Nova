# PCN-0221: Bounded ISO BMFF Media-time Conversion

## Status

Implemented and verified as a non-physical timing-conversion slice.

## Implemented boundary

- `Int128` media-unit and `mdhd` timescale conversion;
- overflow-free `BigInteger` intermediate scaling;
- explicit toward-zero sub-tick rounding; and
- bounded managed magnitude with distinct failure results.

## Verification

Managed tests cover positive and negative conversion, both fractional signs,
extreme `Int128` input, exact configured magnitude, zero and excessive limits,
and missing media timing.

No native runtime, payload read, output device, readiness capture, diagnostic,
probe, or physical audio/video path was exercised.

## Remaining work

Presentation-interval conversion, edit lists, packet projection, and decoder
composition remain unimplemented.
