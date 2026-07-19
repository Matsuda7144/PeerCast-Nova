# ADR-0080: Late Reservation Disposition Boundary

## Status

Accepted for the 0.0.7e technical boundary only.

## Context

0.0.7d converts retained timed-dispatch late results into retention, video-drop-candidate,
or audio-recovery-required outcomes. The scheduler reservation still needs a single owner
that applies those outcomes without duplicating acknowledgement rules.

## Decision

`PlaybackLateReservationDispositionBoundary` receives one existing scheduler reservation,
the matching late dispatch result, and the expected media generation. It delegates the
policy choice to 0.0.7d and completes the reservation exactly once.

- retained video and retained audio complete with `consume = false`;
- audio recovery required completes with `consume = false`;
- reset, invalid, disposed, and failure paths complete with `consume = false`;
- only an explicit video drop candidate completes with `consume = true`.

The boundary records a bounded numeric video-drop count and latches reset/fault states.
It does not select keyframes, inspect future frames, resample or stretch audio, dispatch
to an output device, or own the injected coordination boundary.

## Consequences

0.0.7e establishes the first explicit late-video consumption rule while preserving all
other late items. A later milestone must define audio recovery effects and integration
with the continuous pump. Complete A/V synchronization and real-media playback remain
pending.
