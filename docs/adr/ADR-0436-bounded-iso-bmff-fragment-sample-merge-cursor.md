# ADR-0436: Bounded ISO BMFF Fragment Sample Merge Cursor

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Project an immutable cursor at any index from zero through the exact sample
count of one bounded fragment sample presentation merge. Retain the exact merge
source and expose the next index, total count, remaining count, completion, and
current sample metadata. Treat the one-past-last index and an empty source as
complete, and reject negative or beyond-end indexes.

## Non-goals

Cursor mutation, acknowledgement, delivery, clock conversion, scheduling,
discontinuity handling, `mdat` or payload access, decoder packet composition,
native execution, rendering, or physical execution are not included.
