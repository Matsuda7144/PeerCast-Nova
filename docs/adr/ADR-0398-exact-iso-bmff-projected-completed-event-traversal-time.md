# ADR-0398: Exact ISO BMFF Projected Completed-Event Traversal Time

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply the existing exact rational traversal-time projector to the ADR-0397
acknowledgement view. Retain the complete projected source, every exact
acknowledgement identity, and each event's start, exclusive end, and duration
as numerators over the exact movie-derived seconds denominator.

## Non-goals

Clock-rate conversion, integral rounding, clock acquisition, delivery,
scheduling, runtime access, native execution, rendering, or physical execution
are not included.
