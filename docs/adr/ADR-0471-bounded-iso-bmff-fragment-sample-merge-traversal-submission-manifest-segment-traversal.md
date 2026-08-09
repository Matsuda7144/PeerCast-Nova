# ADR-0471: Bounded ISO BMFF Fragment Sample Merge Traversal Submission Manifest Segment Traversal

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Traverse one valid manifest-segment cursor exhaustively into an immutable exact
cursor-step chain. Enforce a caller-configurable nonnegative step limit bounded
by the existing 65,536 hard limit before allocation, retain the exact initial
and final cursors, and validate every source, segment, and next-index transition.
Resumed and already-terminal cursors are supported.

## Non-goals

Acknowledgement, completion certification, packet composition, payload access,
authorization, runtime lookup, invocation, dispatch, delivery, native
execution, rendering, or physical execution are not included.
