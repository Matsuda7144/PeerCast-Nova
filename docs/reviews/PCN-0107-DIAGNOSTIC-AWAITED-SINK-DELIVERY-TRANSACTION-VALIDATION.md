# PCN-0107 Diagnostic Awaited Sink Delivery Transaction Validation

## Scope

0.0.8o connects the bounded H.264/AAC diagnostic decode path to the existing
serial awaited decoded-frame sink delivery boundary.

## Required verification

- Video send, copy, and awaited delivery occur before audio send, copy, and awaited delivery.
- `Copied` requires one live frame of the expected media kind.
- `NeedMoreInput` requires no frame and performs no sink delivery for that stream.
- Video uses sequence `0`; audio uses sequence `1`; discontinuity is `None`.
- The same caller-owned sink and cancellation token are forwarded.
- Every copied frame is disposed after the awaited callback and before transaction return.
- Cancellation, rejected delivery, contradictory state, and faults dispose the complete aggregate lease.
- Success transfers the same aggregate lease to completion.
- No payload, copied frame, queue, scheduler, demux, production activation, redistribution, or approval-state change is added.

## Automated coverage

Focused tests cover two-stream ordering, video `NeedMoreInput`, expected-media
validation, cancellation, sink failure, pre-cancellation, exception cleanup,
invalid codec rejection, frame disposal, sink forwarding, and lease ownership.

CI #262 passed restore, Release build, the full test suite, and format
verification on both Windows and Ubuntu.

Status: Verified.
