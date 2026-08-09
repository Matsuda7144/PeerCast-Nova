# ADR-0392: Bounded ISO BMFF Projected Completed Merge Events

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply the existing completed merge-event projector to one exact ADR-0391 step
view using a caller-bounded event limit. Retain the exact step-view projection
and order every complete edit appearance by overflow-safe rational movie time,
then global merge index and appearance index, without partial results.

## Non-goals

Cursor advance, traversal, acknowledgement, clock conversion, delivery,
scheduling, runtime access, native execution, rendering, or physical execution
are not included.
