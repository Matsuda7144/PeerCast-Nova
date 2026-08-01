# PCN-0223: Bounded ISO BMFF Edit-list Parser

## Status

Implemented and verified as a non-physical parser slice.

## Implemented boundary

- exact version 0 and 1 `elst` payload parsing;
- bounded immutable entry projection;
- signed media time and raw 16.16 rate preservation;
- empty-edit identification; and
- checked total segment-duration projection.

## Verification

Managed tests cover both versions and widths, rates, empty and nonempty edits,
empty tables, extended and extends-to-end headers, aggregate overflow, both
configuration bounds, entry limits, exact payload length, version, flags,
type, declared shortage, and trailing input.

No native runtime, payload read, output device, readiness capture, diagnostic,
probe, or physical audio/video path was exercised.

## Remaining work

Edit application, movie/media timescale conversion, rate policy, packet
projection, and decoder composition remain unimplemented.
