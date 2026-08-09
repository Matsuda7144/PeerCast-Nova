# ADR-0482: Exact ISO BMFF Regular Movie Scheduler Ingress

## Status

Accepted as the managed-frame ownership transfer from ADR-0481 into the
existing bounded playback scheduler.

## Decision

Consume one exact ADR-0481 native decode-progress owner and one existing
bounded playback scheduler before decode progress has an active frame. Accept
only the exact active `FrameCopied` completion at the next monotonic frame
sequence. Require the completion role to agree with the copied frame media
kind, detach the managed frame exactly once, release the completion back to the
progress owner, and transfer the frame to the scheduler with the next monotonic
scheduler sequence and no inferred discontinuity.

Treat only `Enqueued` and `EnqueuedAfterDroppingOldest` as accepted transfers.
Advance both sequences and the accepted-transfer count only after acceptance.
A cancellation observed before detach retains the exact completion and frame
for retry. Any source, sequence, role, ownership, limit, enqueue, or operation
failure is terminal and disposes the scheduler before the decode-progress
aggregate. Keep enqueue operations injectable for inert managed verification.

## Consequences

Regular MP4 managed decoded frames can now enter the existing bounded
scheduler without duplicate ownership or speculative conversion. The
scheduler remains responsible for its established queue limits, policies, and
eventual delivery-time canonical conversion.

## Non-goals

Running the scheduler, canonical sink delivery, D3D11 or WASAPI output,
application lifecycle composition, discontinuity inference, runtime discovery
or loading, native execution, and physical execution are not included.
