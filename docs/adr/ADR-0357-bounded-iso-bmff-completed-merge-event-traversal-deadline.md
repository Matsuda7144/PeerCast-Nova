# ADR-0357: Bounded ISO BMFF Completed Merge-Event Traversal Deadline

## Status

Accepted as a post-0.0.10e non-physical managed metadata slice.

## Decision

Retain each exact ADR-0356 timeline interval as ready and exclusive-expiration
deadline metadata. Require exact source order and a nonempty interval, returning
no partial projection.

## Non-goals

Clock acquisition or observation, eligibility, dispatch, delivery, native
execution, rendering, or physical execution are not included.
