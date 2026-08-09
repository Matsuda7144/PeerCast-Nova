# ADR-0466: Exact ISO BMFF Fragment Sample Merge Traversal Submission Manifest Source Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Bind one exact submission manifest back to the exact consumer-batch projection
at the root of its intent, classification, and selection chain. Require
reference identity across the complete immutable chain and reject a manifest
rebuilt from any foreign batch.

## Non-goals

Payload bytes or ownership, authorization, scheduling, runtime lookup,
capability access, invocation, dispatch, delivery, native execution, rendering,
or physical execution are not included.
