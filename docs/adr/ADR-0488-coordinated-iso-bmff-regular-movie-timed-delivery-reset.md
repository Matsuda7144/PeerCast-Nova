# ADR-0488: Coordinated ISO BMFF Regular Movie Timed Delivery Reset

## Status

Accepted as the quiesced generation-reset owner around ADR-0486 timed delivery
and ADR-0487 Windows output reconfiguration.

## Decision

Allow a reset-capable timed-delivery driver to consume the exact Windows output
sink and output reconfiguration that belong to its canonical delivery. Factory
creation requires reference-exact sink/output association, matching initial
video and audio generations, ready output state, accepting sink and delivery
state, and timing admission.

After delivery reports `ResetRequired`, acquire the same exclusive gate used by
one-frame driving. Reject transfer and any other drive or reset until the
operation ends. Under that quiescence:

1. reconfigure every output layer to the requested role generations through
   ADR-0487;
2. commit those exact generations to the canonical Windows sink;
3. reset the existing timing boundary and commit the same generations to the
   retained scheduler delivery;
4. restore accepting state and retain the original scheduler reservation for
   its next timing decision.

If timing reset is cancelled or rejects after output commit, reconfigure output
and sink back to the exact previous generations using a non-cancellable
cleanup path. A complete rollback leaves `ResetRequired` intact for retry. Any
partial timing state, output rollback failure, sink commit mismatch, disposed
dependency, or unexpected result faults and closes the aggregate. Successful
reset resets preferred scheduling to video and clears the last timing decision.

## Consequences

Generation mismatch and discontinuity no longer require abandoning the queued
decoded frame. Output, sink, timing, and delivery generation facts either
commit together or return to their exact previous coherent state.

## Non-goals

Decoder-context flush/reopen, late-audio recovery, device reactivation,
continuous pumping, `Nova.App` integration, native invocation during tests,
readiness capture, and physical playback are not included.
