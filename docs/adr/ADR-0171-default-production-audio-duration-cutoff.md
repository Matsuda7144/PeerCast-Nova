# ADR-0171: Default Production Audio-duration Cutoff

## Status

Accepted as a post-0.0.10e non-physical production policy.

## Context

ADR-0167 provides duration ownership, ADR-0169 provides conservative
sample-derived audio duration, and ADR-0170 permits an audio-only scope without
inventing video cadence.

The current production decoder supports AAC audio. A normal AAC frame with
1,024 samples at 48 kHz owns approximately 21.3334 ms after conservative tick
rounding. The default scheduler capacity of 16 frames alone can therefore admit
roughly 341 ms of such audio, before considering mixed video occupancy.

## Decision

The default `PlaybackProductionDecoderGenerationFactoryOptions` selects:

- `MaximumOwnedDuration = 250 ms`;
- `DurationMediaScope = AudioOnly`;
- `DeriveUnknownAudioDurationFromSamples = true`;
- the existing `DropIncoming` full policy.

At 48 kHz this admits eleven 1,024-sample frames (about 234.6674 ms) and rejects
the twelfth by the existing full policy. Other valid sample rates and counts
are governed by their actual or conservatively derived duration.

Video remains bounded by count and owned bytes and receives no inferred
duration.

The controlled physical runner constructs its scheduler options explicitly. It
retains no duration cutoff, `All` scope, and no audio derivation, preserving the
completed evidence behavior and authorization boundary.

## Consequences

Ordinary production generations now have a finite decoded-audio duration bound
in addition to count and byte bounds. Callers can still provide an explicitly
reviewed scheduler configuration.

The overall all-media duration policy remains incomplete because unknown video
duration is not inferred or bounded by media time.

## Non-goals

No physical rerun, controlled-command behavior change, video timing inference,
A/V synchronization, resampling, drift correction, native reset, or packaging
change is included.
