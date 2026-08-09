# ADR-0353: Exact ISO BMFF Completed Merge-Event Traversal Time

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0352 retains every exact completed traversal acknowledgement and event, but
its edit-appearance intervals are still expressed as rational movie units.

## Decision

Resolve the one exact `mvhd` timescale from the completed traversal's root
selection. Require it to be nonzero and require every event packet to retain
that exact movie timescale. Retain each ADR-0352 acknowledgement by reference
and project rational seconds by preserving the event numerators and multiplying
its media-timescale denominator by the movie timescale in `ulong`.

Validate the bounded event count and every nonnegative nonempty interval. Accept
a certified zero-event view as an empty time projection. Fail closed without a
partial result for missing input, invalid timescale, mismatch, or invalid event.

## Consequences

Every certified completed traversal event has exact rational seconds metadata
with its full acknowledgement provenance intact.

## Non-goals

Clock rate, integral rounding, clock acquisition, scheduling, consumption,
delivery, native packet submission, decoder execution, rendering, or physical
execution are not included.
