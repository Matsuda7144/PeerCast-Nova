# ADR-0167: Opt-in Decoded-buffer Duration Cutoff

## Status

Accepted as a post-0.0.10e non-physical implementation slice.

## Context

`BoundedPlaybackScheduler` already bounds owned decoded frames by count and
aggregate plane bytes. Those bounds remain valid when decoder metadata has no
duration, but a caller that requires a media-duration budget previously had no
strict scheduler contract.

Unknown frame duration is valid in the existing decoder contract. Enabling a
duration cutoff implicitly, or assigning a new default value, would therefore
change existing production and controlled-command behavior.

## Decision

Add an optional `MaximumOwnedDuration` to `PlaybackSchedulerOptions`.

When the option is absent, scheduler behavior is unchanged and duration is not
part of ownership accounting. When it is present:

- the value must be positive and no greater than ten minutes;
- every incoming frame must have a known positive duration unless a later
  reviewed exact derivation is explicitly selected;
- a single frame longer than the configured cutoff is disposed and returned as
  `FrameDurationTooLarge`;
- an unknown duration is disposed and returned as `DurationUnavailable`;
- aggregate duration uses the existing `DropOldest`, `DropIncoming`, or
  `Reject` full policy;
- queued, reserved, and in-flight frames remain duration-owned until scheduler
  ownership ends; and
- `PlaybackSchedulerSnapshot.OwnedDuration` exposes the current accounting.

The duration is the aggregate of decoded-frame durations. It is a resource
budget, not an A/V timeline-span or synchronization calculation.

The production generation factory accepts and validates the option. ADR-0171
later selects a 250 ms audio-only default while leaving all-media policy
incomplete.

ADR-0169 later adds an opt-in exact audio derivation from sample count and
sample rate. It does not derive video duration or change this ADR's default.

ADR-0170 later adds an explicit audio-only scope. The default remains strict
all-media accounting.

## Consequences

Callers can opt into a strict decoded-duration cutoff without weakening count
or byte bounds and without changing existing default behavior. Selecting a
default production value requires a later policy decision informed by unknown
metadata handling and normal stream characteristics.

No native runtime, Windows output, readiness helper, physical command, FFmpeg
distribution, installer, signing, or updater behavior changes.

## Non-goals

This slice does not complete production duration-policy selection, A/V
synchronization, resampling, drift correction, discontinuity reset, parser
work, or physical playback validation.
