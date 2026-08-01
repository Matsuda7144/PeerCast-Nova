# PCN-0205: ISO BMFF Sync-sample Range Reconciliation

## Status

Implemented and verified as a non-physical sync-sample slice.

## Implemented boundary

- optional parsed `stss` input;
- one-based sample-number upper-bound validation;
- distinct out-of-range failure; and
- present empty and nonempty table projection.

## Verification

Managed tests cover an absent table through existing overloads, a present
in-range table, a present empty table, and an out-of-range sample number.

No native runtime, output device, readiness capture, diagnostic, probe, or
physical audio/video path was exercised.

## Remaining work

Seek policy, keyframe inference, packet flag projection, partial-sync
integration, sample lookup, and decoder composition remain unimplemented.
