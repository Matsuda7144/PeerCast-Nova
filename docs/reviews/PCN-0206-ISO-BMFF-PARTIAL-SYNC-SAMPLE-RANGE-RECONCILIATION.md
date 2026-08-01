# PCN-0206: ISO BMFF Partial-sync Sample Range Reconciliation

## Status

Implemented and verified as a non-physical partial-sync slice.

## Implemented boundary

- optional parsed `stps` input;
- one-based partial-sync sample-number upper-bound validation;
- distinct out-of-range failure; and
- present empty and nonempty table projection.

## Verification

Managed tests cover an absent table through existing overloads, a present
in-range table, a present empty table, and an out-of-range sample number.

No native runtime, output device, readiness capture, diagnostic, probe, or
physical audio/video path was exercised.

## Remaining work

Full-sync integration, seek policy, keyframe inference, packet flag
projection, sample lookup, and decoder composition remain unimplemented.
