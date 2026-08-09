# ADR-0473: Exact ISO BMFF Fragment Sample Merge Traversal Submission Manifest Completed Segment View

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Reconstruct the exact segment references traversed by one metadata-only segment
completion certificate. Retain zero-based completed-view index, original source
segment index, exact cursor-step reference, and exact segment reference. Preserve
resumed traversal indexes and reject count or source mismatches. The view is
explicitly non-acknowledging.

## Non-goals

Acknowledgement, submission or delivery completion, packet composition, payload
access, authorization, runtime lookup, invocation, dispatch, delivery, native
execution, rendering, or physical execution are not included.
