# ADR-0293: Bounded ISO BMFF Program Config Interleaved PCM Reorder

## Status

Accepted as a post-0.0.10e non-physical managed-buffer slice.

## Context

ADR-0291 provides an exact PCE source-to-platform channel permutation, but does
not apply it. Canonical decoded audio frames already provide validated complete
interleaved PCM payloads and bounded metadata.

## Decision

Add a pure managed materializer for canonical signed-16 or float-32 interleaved
PCM frames. Require exact channel-count agreement with one reorder plan, enforce
a configurable owned-payload byte limit with the existing managed-frame hard
limit, copy every complete sample channel according to the plan, and retain the
exact source frame and plan alongside the resulting owned frame.

Preserve sequence, timing, discontinuity, format, rate, channel count, and sample
count exactly. Missing inputs, count mismatch, invalid limits, and limit excess
produce no output.

## Consequences

The reviewed PCE permutation can now be applied deterministically without native
access or callback-lifetime borrowing.

## Non-goals

Format conversion, resampling, mixdown, decoder integration, native calls,
device activation, audio rendering, and physical execution are not included.
