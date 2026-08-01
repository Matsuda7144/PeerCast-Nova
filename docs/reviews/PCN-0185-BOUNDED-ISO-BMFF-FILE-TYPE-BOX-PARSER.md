# PCN-0185: Bounded ISO BMFF File-type Box Parser

## Status

Implemented and verified as a non-physical parser prerequisite.

## Implemented boundary

- exact complete-box length validation;
- ordinary, extended-size, and extends-to-end `ftyp` headers;
- major brand and minor version projection;
- exact 4-byte compatible-brand entries;
- default 32-entry and hard 256-entry bounds;
- read-only compatible-brand projection;
- fixed privacy-safe result categories.

## Verification

Managed tests cover ordinary and extended boxes, extends-to-end, multiple and
zero compatible brands, both invalid configuration limits, brand saturation,
wrong box type, truncated headers and boxes, trailing input, and malformed
payload lengths.

No native runtime, output device, readiness capture, diagnostic, or physical
audio/video path was exercised.

## Remaining work

Brand acceptance policy, streaming accumulation, nested-container policy,
movie/sample metadata, fragmented runs, media data, and decoder composition
remain unimplemented.
