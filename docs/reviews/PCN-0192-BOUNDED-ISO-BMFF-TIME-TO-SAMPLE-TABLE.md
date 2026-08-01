# PCN-0192: Bounded ISO BMFF Time-to-sample Table

## Status

Implemented and verified as a non-physical parser slice.

## Implemented boundary

- exact complete `stts` box validation;
- FullBox version 0 with zero flags;
- default 4,096-entry and hard 65,536-entry bounds;
- exact fixed-size run payload validation;
- nonzero sample counts and permitted zero deltas;
- read-only run projection;
- checked total sample count and duration units.

## Verification

Managed tests cover multiple and zero entries, exact aggregates, zero delta,
extended and extends-to-end headers, both invalid limits, entry saturation,
zero sample count, duration overflow, short and extra entry bytes, unsupported
version, nonzero flags, wrong type, declared-size shortage, and trailing input.

No native runtime, output device, readiness capture, diagnostic, or physical
audio/video path was exercised.

## Remaining work

Composition offsets, sample sizes, chunk mapping, sync samples, fragmented
runs, media data, timestamp expansion, and decoder composition remain
unimplemented.
