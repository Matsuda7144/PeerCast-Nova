# PCN-0190: ISO BMFF Track-header Identity Projection

## Status

Implemented and verified as a non-physical parser slice.

## Implemented boundary

- exact complete `tkhd` box validation;
- FullBox versions 0 and 1;
- exact version-specific payload lengths;
- four defined lifecycle/aspect flag projections;
- unsigned creation, modification, track ID, and duration;
- nonzero track ID;
- explicit unknown-duration sentinel;
- zero required reserved fields;
- raw unsigned 16.16 width and height.

## Verification

Managed tests cover both versions, values beyond 32 bits, every defined flag
projection, unknown-duration sentinels, extended and extends-to-end headers,
zero track ID, unknown flag bits, every required reserved field, unsupported
version, wrong type, declared-size shortage, trailing input, and adjacent
invalid payload lengths.

No native runtime, output device, readiness capture, diagnostic, or physical
audio/video path was exercised.

## Remaining work

Matrix/presentation policy, handler-to-track composition, sample descriptions,
sample metadata, fragmented runs, media data, and decoder composition remain
unimplemented.
