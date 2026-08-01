# PCN-0193: Bounded ISO BMFF Composition-time Table

## Status

Implemented and verified as a non-physical parser slice.

## Implemented boundary

- exact complete `ctts` box validation;
- FullBox versions 0 and 1 with zero flags;
- default 4,096-entry and hard 65,536-entry bounds;
- exact fixed-size run payload validation;
- nonzero sample counts;
- unsigned version 0 and signed version 1 offset projection;
- read-only run projection and bounded total sample count.

## Verification

Managed tests cover version 0 zero and maximum unsigned offsets, version 1
minimum/negative/maximum signed offsets, empty tables, extended and
extends-to-end headers, both invalid limits, entry saturation, zero sample
count, short and extra entry bytes, unsupported versions, nonzero flags, wrong
type, declared-size shortage, and trailing input.

No native runtime, output device, readiness capture, diagnostic, or physical
audio/video path was exercised.

## Remaining work

Decode/composition run reconciliation, timestamp expansion, edit lists, sample
sizes, chunk mapping, fragmented runs, media data, and decoder composition
remain unimplemented.
