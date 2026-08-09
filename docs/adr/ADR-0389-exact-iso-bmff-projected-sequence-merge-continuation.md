# ADR-0389: Exact ISO BMFF Projected-Sequence Merge Continuation

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Continue an exact truncated ADR-0388 traversal projection, or a prior exact
continuation projection, from its final cursor state with the same tie policy
and a caller-bounded additional step limit. Retain the root cursor projection,
exact source projection, cumulative step count, and new segment without
re-emitting prior steps.

## Non-goals

Automatic continuation loops, policy changes, completion certification, event
projection, delivery, scheduling, runtime access, or physical execution are not
included.
