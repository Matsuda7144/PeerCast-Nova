# ADR-0387: Exact ISO BMFF Selected-Sequence Initial Cursor-Set Projection

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply the existing immutable cursor-set factory to one exact ADR-0386 selection
projection. Retain the exact selection source and create index-zero cursors for
exactly the explicitly selected video and/or audio sequences without advancing,
reading, comparing, merging, or consuming an entry.

## Non-goals

Cursor advance, cross-track comparison or merge, traversal, acknowledgement,
delivery, scheduling, runtime access, native execution, rendering, or physical
execution are not included.
