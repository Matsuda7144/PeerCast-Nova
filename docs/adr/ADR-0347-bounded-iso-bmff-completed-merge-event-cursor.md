# ADR-0347: Bounded ISO BMFF Completed Merge-Event Cursor

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0346 owns every complete edit appearance from one certified packet merge in
a bounded deterministic event order. Callers need immutable traversal metadata
without mutating the projection or implying that an event was consumed.

## Decision

Create an immutable cursor at event index zero over one exact ADR-0346
projection. Retain the projection by reference, expose the next index, remaining
count, and exact exhaustion, and reject projections above the 131,072-event hard
limit.

Advance returns the exact current event and a new cursor at the next index while
leaving the source cursor unchanged. Exhausted input returns no event or
successor. Missing and out-of-range cursors fail closed.

## Consequences

The complete presentation-event order can be traversed one exact immutable
event at a time with explicit remaining and exhaustion state.

## Non-goals

Acknowledgement, consumption or delivery, clock acquisition, scheduling,
native packet submission, decoder execution, rendering, or physical execution
are not included.
