# PCN-0106 Diagnostic Managed Format Conversion Transaction Validation

## Scope

0.0.8n connects the bounded H.264/AAC diagnostic transaction to the existing managed YUV and planar PCM conversion boundary.

## Verified behavior

- Video send, copy, conversion, then audio send, copy, conversion occur in fixed order.
- `Converted` requires exactly one callback of the expected media kind.
- `NeedMoreInput` requires no copied frame or output summary.
- Copied input frames are disposed before transaction return.
- Rejected, contradictory, and faulted paths dispose the complete aggregate lease.
- Success retains summaries only and transfers the same lease to completion.
- No converted payload, native pointer, sink delivery, queue, demux, production activation, or approval-state change is added.

## Automated verification

CI #259 passed on Windows and Ubuntu. Restore, Release build, the full test suite, and format verification all completed successfully. Focused tests cover video-only progress, two-stream conversion, callback-kind contradiction, exception cleanup, invalid codec rejection, ordering, frame disposal, and lease ownership.

Status: Verified.
