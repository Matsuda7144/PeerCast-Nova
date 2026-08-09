# ADR-0468: Bounded ISO BMFF Fragment Sample Merge Traversal Submission Manifest Segments

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Partition one summary-validated manifest into immutable contiguous segments when
adjacent entries retain the same fragment batch index and sequence number.
Retain exact entry references, zero-based segment index, half-open manifest
index range, fragment identity, and exact total sample bytes. Preserve manifest
order and emit no synthetic segment for an empty manifest.

## Non-goals

Packet composition, payload access or coalescing, authorization, scheduling,
runtime lookup, invocation, dispatch, delivery or completion claims, native
execution, rendering, or physical execution are not included.
