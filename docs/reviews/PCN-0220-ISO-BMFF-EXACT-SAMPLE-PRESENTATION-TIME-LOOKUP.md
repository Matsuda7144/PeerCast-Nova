# PCN-0220: Exact ISO BMFF Sample Presentation-time Lookup

## Status

Implemented and verified as a non-physical timing-composition slice.

## Implemented boundary

- composed decode and composition lookup;
- exact signed `Int128` presentation start and exclusive end;
- duration and composition-source preservation; and
- fail-closed reconciliation and sample bounds.

## Verification

Managed tests cover absent, positive, and negative composition offsets,
independent run boundaries, a time beyond `long.MaxValue`, both
sample-number bounds, and decode or composition reconciliation failures.

No native runtime, payload read, output device, readiness capture, diagnostic,
probe, or physical audio/video path was exercised.

## Remaining work

Timescale conversion, edit lists, timestamp magnitude policy, packet
projection, and decoder composition remain unimplemented.
