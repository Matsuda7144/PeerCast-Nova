# ADR-0169: Sample-derived Audio Duration Accounting

## Status

Accepted as a post-0.0.10e non-physical duration-policy prerequisite.

## Context

ADR-0167 adds a strict opt-in decoded-buffer duration cutoff. FFmpeg may report
an unknown frame duration even when an audio frame has validated sample count
and sample rate. Rejecting such audio is deterministic but prevents a future
production cutoff from using information that is already exact at the decoded
buffer boundary.

Video frames have no equivalent exact duration derivation from the currently
retained metadata and must not be assigned a guessed cadence.

## Decision

Add `DeriveUnknownAudioDurationFromSamples` to
`PlaybackSchedulerOptions`.

The option is valid only when `MaximumOwnedDuration` is also selected. When it
is enabled and an audio frame has unknown duration, the scheduler computes:

`ceil(sample count * TimeSpan ticks per second / sample rate)`

Rounding is upward so duration accounting never understates owned audio. The
existing validated positive sample count and sample rate are required.

Known duration remains authoritative. Unknown video duration and malformed
audio metadata remain `DurationUnavailable`. The derived value participates in
the same single-frame cutoff, aggregate full policy, snapshot, reservation,
in-flight ownership, and cleanup rules as a known duration.

Both the strict cutoff and audio derivation are configurable. ADR-0171 later
selects derivation for the default 250 ms production audio policy.

ADR-0170 later permits this accounting to be scoped to audio while video
continues to use the existing count and byte bounds.

## Consequences

A future production policy can account for decoded audio whose FFmpeg duration
field is absent without inventing video timing or changing default scheduler
behavior.

## Non-goals

No video cadence inference, timestamp interpolation, A/V synchronization,
resampling, drift correction, default cutoff selection, native activation, or
physical output is included.
