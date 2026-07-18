# ADR-0079: Late Dispatch Coordination Boundary

## Status

Accepted for the 0.0.7d technical boundary only. FFmpeg backend activation,
bundling, and distribution remain unapproved, and license review remains
`Pending`. This Accepted status does not approve any of them.

## Context

0.0.7b reports retained scheduler reservations as `VideoLate` or `AudioLate`.
0.0.7c maps the corresponding timing result to a safe retention decision or an
explicit video-drop/audio-recovery recommendation. A small coordination boundary
is required so callers do not duplicate the mapping between the dispatch and
policy vocabularies.

## Decision

`PlaybackLateDispatchCoordinationBoundary` accepts only the exact pairs
`VideoLate`/`Video` and `AudioLate`/`Audio`. It translates them to the existing
0.0.7c policy request and returns one of:

- `RetainedVideoLate`;
- `RetainedAudioLate`;
- `VideoDropCandidate`;
- `AudioRecoveryRequired`.

The boundary does not own or receive a scheduler reservation and therefore
cannot acknowledge, consume, release, or dispose it. It performs no drop,
resampling, stretching, output reset, timing calculation, delay, or retry.
Undefined values, non-late dispatch results, media/result mismatches, negative
sequence/generation values, generation mismatch, and discontinuity fail closed.
Generation mismatch and discontinuity latch `ResetRequired` until explicit
reset. Reset updates the coordination and injected policy generations together.
Disposal is idempotent and does not dispose the injected policy.

## Consequences

0.0.7d establishes one vocabulary bridge from timed dispatch to late policy
without adding effects. A later milestone must retain the scheduler reservation
through coordination and define any explicit acknowledgement or recovery effect.
Actual video dropping, keyframe selection, audio resampling/stretch, drift
correction, precise timers, complete A/V synchronization, FFmpeg activation,
distribution approval, and real-media playback remain pending.
