# ADR-0076: A/V Timing Decision Boundary

## Status

Accepted for the 0.0.7a technical boundary only. FFmpeg backend activation,
bundling, and distribution remain unapproved, and license review remains
`Pending`. This Accepted status does not approve any of them.

## Context

0.0.6q supplies monotonic playback-clock policy, 0.0.6r owns bounded queued
items, 0.0.6y dispatches one item, and 0.0.6z drives a finite pump. A separate
decision boundary is needed before callers add timing actions. It must not
sleep, reorder, consume, drop, duplicate, resample, or take lower ownership.

## Decision

`PlaybackAvTimingDecisionBoundary.DecideAsync` evaluates exactly one borrowed
item. It validates media kind, known non-negative timestamp, optional positive
duration, sequence, discontinuity, and the media-specific generation before
reading an injected monotonic position source. The production source directly
reuses `IPlaybackMonotonicClock`; it has no wall-clock or native-device access.

The checked delta is presentation timestamp minus playback position. A delta
from negative late tolerance through positive early tolerance, inclusively,
is `ProcessNow`. A greater positive delta through `MaximumWait` is `Wait` with
the exact finite duration. A greater wait is `WaitLimitExceeded`. A delta below
the relevant late boundary is `VideoLate` or `AudioLate`. Decisions never
change scheduler ownership.

Each early/late tolerance is zero through five seconds. `MaximumWait` is
greater than zero through two seconds and cannot be smaller than either early
tolerance. Negative or excessive values are rejected. Clock-not-started is a
fixed result; negative, backward, undefined, overflowing, or exceptional clock
observations fail closed.

Any defined non-`None` discontinuity latches `ResetRequired` without resetting
the clock, rebasing the relative playback origin, or consuming the item. Only a
caller-initiated explicit reset may rebase the origin. Undefined discontinuity
fails closed.
Video/audio generations are independent and must match exactly. Reset invokes
the injected seams in audio-generation, video-generation, then timing-origin
order. Production origin reset clears the existing monotonic origin; the next
normal read establishes a new origin at position zero. Generations and Ready
state commit only after all three operations succeed. A video-generation
failure receives one audio rollback attempt. An origin-reset failure receives
one video-then-audio generation rollback attempt, but still exposes
`PartialResetFault` and `Faulted` because partial origin mutation cannot be
excluded. Undefined results and exceptions fail closed, with no retry loop.
Reset and dispose serialize with decisions. Disposal is idempotent and does not
dispose the clock, position source, scheduler, adapter, processor, or activation
resources.

## Consequences

0.0.7a implements production timing decisions, not timing effects. Precise
timers, actual delay, frame drop or duplication, audio resampling/stretch,
drift correction, complete A/V synchronization, automatic seek, FFmpeg
activation or codec wiring, distribution/licensing approval, and real-media
playback remain unimplemented.
