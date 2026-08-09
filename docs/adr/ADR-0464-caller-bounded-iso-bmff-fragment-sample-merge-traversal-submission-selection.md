# ADR-0464: Caller-Bounded ISO BMFF Fragment Sample Merge Traversal Submission Selection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Retain at most one caller-specified positive bounded count of `Include`-classified
submission intents as an immutable source-order prefix. Preserve exact source
indexes and classification references and report total included count and
truncation without reordering or treating inclusion as authorization.

## Non-goals

Authorization, payload selection or ownership, scheduling, runtime lookup,
capability access, invocation, dispatch, delivery, native execution, rendering,
or physical execution are not included.
