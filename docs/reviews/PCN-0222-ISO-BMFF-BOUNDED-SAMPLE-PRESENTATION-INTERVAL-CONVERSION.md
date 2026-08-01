# PCN-0222: Bounded ISO BMFF Sample Presentation-interval Conversion

## Status

Implemented and verified as a non-physical timing-composition slice.

## Implemented boundary

- exact sample-presentation lookup composition;
- common-timescale start and exclusive-end conversion;
- endpoint-derived managed duration; and
- distinct structural and conversion failures.

## Verification

Managed tests cover exact and fractional intervals, negative starts, both
sample-number bounds, reconciliation failure, missing timing, invalid
conversion configuration, and end magnitude rejection.

No native runtime, payload read, output device, readiness capture, diagnostic,
probe, or physical audio/video path was exercised.

## Remaining work

Edit lists, packet projection, payload reads, and decoder composition remain
unimplemented.
