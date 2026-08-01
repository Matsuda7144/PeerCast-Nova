# ADR-0170: Audio-scoped Decoded-duration Cutoff

## Status

Accepted as a post-0.0.10e non-physical production-policy prerequisite.

## Context

ADR-0167 defines a strict aggregate decoded-duration cutoff and ADR-0169 can
derive missing audio duration from validated samples. Unknown video duration
still cannot be derived without inventing a cadence. Requiring one combined
duration policy to reject such video would prevent adoption of the safe audio
portion, while accepting unknown values in an all-media cutoff would weaken its
contract.

Video remains independently bounded by scheduler frame count and owned bytes.

## Decision

Add `PlaybackSchedulerDurationMediaScope` with:

- `All`, the existing strict behavior and default;
- `AudioOnly`, which applies duration accounting and the duration full policy
  only to audio frames.

With `AudioOnly`, video contributes zero owned duration whether its metadata
duration is known or unknown. It remains fully subject to count, byte,
sequence, ownership, reservation, and disposal rules.

Audio still requires known duration or the explicit ADR-0169 sample derivation.
The non-default scope is valid only when `MaximumOwnedDuration` is selected.
Undefined scope values fail configuration validation in both the scheduler and
production generation factory.

No production cutoff value or scope is selected by default in this slice.

ADR-0171 later selects a 250 ms audio-only production default.

## Consequences

A later production policy may adopt a strict audio buffer-duration cutoff
without fabricating video timing or removing existing video memory bounds.
An all-media timeline-duration policy remains a separate unresolved problem.

## Non-goals

No video inference, A/V synchronization, default production cutoff, physical
output, native reset, parser, recorder, installer, signing, or updater behavior
is added.
