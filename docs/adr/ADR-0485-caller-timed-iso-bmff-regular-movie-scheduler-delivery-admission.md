# ADR-0485: Caller-Timed ISO BMFF Regular Movie Scheduler Delivery Admission

## Status

Accepted as the reservation-side timing admission before ADR-0483 canonical
sink delivery.

## Decision

Allow ADR-0483 delivery to borrow one caller-supplied
`PlaybackAvTimingDecisionBoundary` with explicit nonnegative video and audio
generations. After reserving and validating one scheduler frame, construct the
exact timing item from its media kind, presentation timestamp, optional
duration, sequence, discontinuity, and matching role generation. Perform this
decision before decoder-context selection, canonical conversion, or sink
invocation.

Proceed to canonical delivery only for `ProcessNow`. Retain the exact
reservation for `Wait`, `ClockNotStarted`, and `AudioLate`, exposing audio late
as an explicit recovery-required result. Consume `VideoLate` as a scheduler
drop, increment a bounded video-drop count, and prefer audio next. Retain the
reservation and enter a fixed reset-required state for discontinuity or
generation mismatch. Propagate cancellation. Treat malformed timing metadata,
undefined decisions, wait-limit rejection, and timing failure as fail-closed.
The caller remains responsible for honoring the reported wait duration before
re-driving delivery.

## Consequences

Regular MP4 scheduling no longer misclassifies timing wait, late-video drop,
late-audio recovery, or clock/reset prerequisites as canonical sink failure.
Timing and output generations remain caller-owned and explicit.

## Non-goals

Delay execution, timing or output reset orchestration, audio recovery,
device-loss recovery, application pumping, D3D11 or WASAPI activation during
verification, native execution, and physical execution are not included.
