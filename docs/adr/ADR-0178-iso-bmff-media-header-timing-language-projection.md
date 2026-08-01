# ADR-0178: ISO BMFF Media-header Timing and Language Projection

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

The media-header (`mdhd`) box supplies the timescale and duration used by one
track's media timeline. It also contains a packed three-character ISO-639
language value. Both version-dependent timing widths and the packed language
need exact validation before track/sample composition can rely on them.

## Decision

Add a pure managed parser for one complete `mdhd` box that:

- accepts normal, extended-size, and extends-to-end box headers;
- requires the declared size to match the supplied complete box;
- accepts only FullBox versions 0 and 1 with zero flags;
- requires the exact 24-byte version 0 or 36-byte version 1 payload;
- projects creation, modification, timescale, and duration using the correct
  unsigned widths;
- rejects a zero timescale;
- represents the version-specific all-ones duration sentinel as unknown;
- validates the reserved language bit and each packed 5-bit character as
  lowercase `a` through `z`;
- projects the language as an immutable three-character string; and
- requires the trailing `pre_defined` field to be zero.

No epoch or duration conversion is performed.

## Consequences

Callers can obtain bounded media timing and language metadata without retaining
the input box or guessing invalid packed-language values.

Track identity, handler type, and samples remain unavailable.

## Non-goals

No wall-clock conversion, duration rounding, track-header parsing, handler
classification, sample tables, fragments, media-data association, decoder
composition, recording, or physical execution is included.
