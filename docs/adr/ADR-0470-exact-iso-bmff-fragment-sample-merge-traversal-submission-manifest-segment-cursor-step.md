# ADR-0470: Exact ISO BMFF Fragment Sample Merge Traversal Submission Manifest Segment Cursor Step

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Advance one nonterminal manifest-segment cursor by exactly one segment while
retaining the exact source cursor and segment references and returning a new
cursor over the same segment projection. Reject missing, invalid, foreign, and
terminal cursor states without mutating the source.

## Non-goals

Traversal acknowledgement or completion certification, packet composition,
payload access, authorization, runtime lookup, invocation, dispatch, delivery,
native execution, rendering, or physical execution are not included.
