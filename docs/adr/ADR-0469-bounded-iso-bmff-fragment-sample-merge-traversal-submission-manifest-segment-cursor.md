# ADR-0469: Bounded ISO BMFF Fragment Sample Merge Traversal Submission Manifest Segment Cursor

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Project an immutable cursor over one exact bounded manifest-segment projection.
Retain a caller-specified next-segment index in the inclusive range from zero to
segment count and expose remaining count and an exact current-segment reference.
The terminal position has no current segment and does not claim processing or
delivery completion.

## Non-goals

Source mutation, traversal acknowledgement or completion certification, packet
composition, payload access, authorization, runtime lookup, invocation,
dispatch, delivery, native execution, rendering, or physical execution are not
included.
