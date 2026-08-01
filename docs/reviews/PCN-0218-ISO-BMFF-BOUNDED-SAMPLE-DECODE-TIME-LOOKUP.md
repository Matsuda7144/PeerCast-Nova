# PCN-0218: Bounded ISO BMFF Sample Decode-time Lookup

## Status

Implemented and verified as a non-physical timing-lookup slice.

## Implemented boundary

- reconciled sample-count validation;
- one-based sample bounds;
- run-relative decode start and duration calculation; and
- exclusive-end projection without per-sample expansion.

## Verification

Managed tests cover samples within and across runs, zero deltas, a maximum-size
uniform run, both sample-number bounds, reconciliation mismatch, missing
inputs, and an empty table.

No native runtime, payload read, output device, readiness capture, diagnostic,
probe, or physical audio/video path was exercised.

## Remaining work

Timescale conversion, composition offsets, presentation timestamps, edit
lists, packet projection, and decoder composition remain unimplemented.
