# ADR-0081: Late-Aware Scheduler Decision Boundary

## Status

Accepted for the 0.0.7f technical boundary only.

## Context

0.0.7b completes a scheduler reservation before returning its late result. That is safe for
an isolated dispatch operation, but it means 0.0.7e cannot receive the same reservation and
apply the selected late disposition. A reservation-preserving integration seam is required
before continuous pumping can use the late policy.

## Decision

`PlaybackLateAwareSchedulerDecisionBoundary` reserves one scheduler item, creates the timing
item, obtains one A/V timing decision, and retains ownership of the reservation until that
decision is mapped.

- `VideoLate` and `AudioLate` are passed with the still-live reservation to 0.0.7e;
- explicit video-drop consumption is the only late path that consumes the reservation;
- process-now and wait decisions are returned as required follow-up actions while retaining
  the reservation;
- reset, invalid, cancellation, disposal, and failure paths fail closed and retain the item;
- only a consumed video-drop advances the deterministic media preference.

The boundary does not dispatch process-now items, perform waiting, own the injected timing or
disposition boundaries, select keyframes, recover audio, or run continuously.

## Consequences

0.0.7f closes the reservation-lifetime gap between timing decisions and late disposition.
A later milestone can combine process-now dispatch, bounded waiting, and this late-aware path
inside the finite continuous pump without reopening reservation completion rules.
