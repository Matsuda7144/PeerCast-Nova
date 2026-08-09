# ADR-0472: Exact ISO BMFF Fragment Sample Merge Traversal Submission Manifest Segment Completion

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Certify that one immutable segment traversal exhausts its exact segment source.
Revalidate exact initial/final source identity, terminal index, step count,
contiguous cursor identity, segment identity, and next indexes. Retain initial,
final, total, and traversed segment counts, including resumed and empty
traversals. This certificate describes metadata traversal only.

## Non-goals

Acknowledgement, submission or delivery completion, packet composition, payload
access, authorization, runtime lookup, invocation, dispatch, delivery, native
execution, rendering, or physical execution are not included.
