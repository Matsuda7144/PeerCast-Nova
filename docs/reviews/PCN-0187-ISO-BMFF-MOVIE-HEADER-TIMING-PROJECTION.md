# PCN-0187: ISO BMFF Movie-header Timing Projection

## Status

Implemented and verified as a non-physical parser slice.

## Implemented boundary

- exact complete `mvhd` box validation;
- FullBox versions 0 and 1 with zero flags;
- exact version-specific fixed payload lengths;
- unsigned creation and modification values;
- nonzero unsigned timescale;
- unsigned duration or explicit unknown-duration sentinel;
- no epoch or managed-duration conversion.

## Verification

Managed tests cover both versions, values beyond 32 bits, both unknown-duration
sentinels, extended and extends-to-end headers, zero timescale, nonzero flags,
unsupported versions, wrong type, truncation, trailing input, and every adjacent
invalid fixed payload length.

No native runtime, output device, readiness capture, diagnostic, or physical
audio/video path was exercised.

## Remaining work

Track timing, handler and sample metadata, fragmented runs, media data,
streaming accumulation, nesting policy, and decoder composition remain
unimplemented.
