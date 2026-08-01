# PCN-0217: Bounded ISO BMFF Sample Media-data Lookup

## Status

Implemented and verified as a non-physical structural-lookup slice.

## Implemented boundary

- sample and whole-file range prevalidation;
- bounded top-level structural traversal;
- automatic containing-`mdat` selection; and
- distinct malformed, limit, and not-found outcomes.

## Verification

Managed tests cover multiple top-level boxes and `mdat` boxes, zero-size sample
ranges, extends-to-end media data, no match, invalid and out-of-file sample
ranges, truncated boxes, box-count limits, invalid configuration, and empty
files.

No native runtime, payload read, output device, readiness capture, diagnostic,
probe, or physical audio/video path was exercised.

## Remaining work

Payload reads, packet projection, cross-sample validation, fragmented runs,
codec selection, and decoder composition remain unimplemented.
