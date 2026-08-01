# PCN-0188: ISO BMFF Media-header Timing and Language Projection

## Status

Implemented and verified as a non-physical parser slice.

## Implemented boundary

- exact complete `mdhd` box validation;
- FullBox versions 0 and 1 with zero flags;
- exact version-specific payload lengths;
- unsigned creation, modification, timescale, and duration;
- explicit unknown-duration sentinel;
- validated three-character packed ISO-639 language;
- zero `pre_defined` requirement.

## Verification

Managed tests cover both versions, values beyond 32 bits, both unknown-duration
sentinels, extended and extends-to-end headers, valid languages, zero and
out-of-range language characters, the reserved language bit, nonzero
`pre_defined`, zero timescale, nonzero flags, unsupported versions, wrong type,
truncation, trailing input, and adjacent invalid payload lengths.

No native runtime, output device, readiness capture, diagnostic, or physical
audio/video path was exercised.

## Remaining work

Track identity, handler classification, sample metadata, fragmented runs, media
data, streaming accumulation, nesting policy, and decoder composition remain
unimplemented.
