# PCN-0216: Bounded ISO BMFF Sample Media-data Containment

## Status

Implemented and verified as a non-physical range-validation slice.

## Implemented boundary

- exact sample-range reuse;
- caller-selected `mdat` type validation;
- overflow-checked absolute payload extent projection; and
- full range containment excluding the box header.

## Verification

Managed tests cover interior and exact payload ranges, header overlap, payload
overrun, zero-size payload-end behavior, wrong and missing boxes, inconsistent
sample ranges, and absolute-offset overflow.

No native runtime, payload read, output device, readiness capture, diagnostic,
probe, or physical audio/video path was exercised.

## Remaining work

Automatic association across multiple media-data boxes, whole-file traversal,
byte reads, fragmented runs, codec selection, and decoder composition remain
unimplemented.
