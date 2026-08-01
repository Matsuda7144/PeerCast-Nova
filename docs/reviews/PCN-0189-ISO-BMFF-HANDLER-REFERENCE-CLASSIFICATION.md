# PCN-0189: ISO BMFF Handler-reference Classification

## Status

Implemented and verified as a non-physical parser slice.

## Implemented boundary

- exact complete `hdlr` box validation;
- FullBox version 0 with zero flags;
- zero fixed reserved fields;
- raw handler type plus Video/Audio/Other classification;
- strict null-terminated UTF-8 name;
- default 255-byte and hard 4,096-byte name bounds;
- immutable name projection.

## Verification

Managed tests cover video, audio, and other handlers; ASCII, non-ASCII, and
empty names; extended and extends-to-end headers; both invalid limits; UTF-8
byte saturation; invalid UTF-8; missing and embedded terminators; every fixed
reserved field; unsupported version; nonzero flags; wrong type; truncation;
declared-size shortage; and trailing input.

No native runtime, output device, readiness capture, diagnostic, or physical
audio/video path was exercised.

## Remaining work

Sample descriptions, codec selection, sample metadata, fragmented runs, media
data, streaming accumulation, nesting policy, and decoder composition remain
unimplemented.
