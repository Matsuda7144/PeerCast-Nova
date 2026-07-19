# ADR-0083: Late-Aware Timed Dispatch Boundary

## Status

Accepted for the 0.0.7g technical boundary only.

## Context

0.0.7b provides bounded wait/redecision and output dispatch, but completes a scheduler
reservation before a late result can reach 0.0.7e. 0.0.7f preserves the reservation across
one timing decision, but intentionally does not wait or dispatch output.

## Decision

`PlaybackLateAwareTimedDispatchBoundary` combines those responsibilities while keeping one
reservation alive until the final result is known.

- `Wait` is bounded by decision count, cumulative duration, and single-delay limits.
- `ProcessNow` dispatches output and consumes only after a successful processed result.
- `VideoLate` and `AudioLate` pass the same reservation to 0.0.7e before completion.
- explicit video-drop disposition consumes the reservation;
- retained late, audio recovery, wait-limit, invalid, cancellation, and output retry paths
  retain the reservation;
- generation mismatch and discontinuity latch `ResetRequired`.

The boundary does not own the injected disposition policy and does not implement a continuous
pump, keyframe selection, audio recovery effects, frame duplication, drift correction, or
complete A/V synchronization.

## Consequences

0.0.7g closes the reservation-lifetime gap between timed scheduling and late disposition.
A later milestone can place this one-item operation behind a bounded continuous pump without
reimplementing reservation completion rules. Controlled audible/visible real-media playback
remains pending.
