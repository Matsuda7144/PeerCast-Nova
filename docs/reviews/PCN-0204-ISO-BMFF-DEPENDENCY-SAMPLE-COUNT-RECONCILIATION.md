# PCN-0204: ISO BMFF Dependency Sample-count Reconciliation

## Status

Implemented and verified as a non-physical dependency slice.

## Implemented boundary

- optional parsed `sdtp` input;
- exact present-`sdtp` entry-count to reconciled sample-count equality;
- distinct dependency-count mismatch; and
- successful dependency-presence projection.

## Verification

Managed tests cover absent dependencies, a matching dependency count, and a
present dependency-count mismatch.

No native runtime, output device, readiness capture, diagnostic, probe, or
physical audio/video path was exercised.

## Remaining work

Dependency interpretation, keyframe inference, packet flag projection,
sync-sample reconciliation, chunk mapping, and decoder composition remain
unimplemented.
