# ADR-0462: Bounded ISO BMFF Fragment Sample Merge Traversal Submission Intent Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Project each exact consumer-batch entry into immutable source-ordered submission
intent metadata. Retain a contiguous submission index, exact batch entry and
request references, and opaque consumer and batch identifiers. Revalidate exact
entry, binding, consumer, and batch identity without accessing payload bytes or
a runtime consumer.

## Non-goals

Authorization, admission, selection, scheduling, runtime registration or
lookup, capability resolution, payload access, invocation, dispatch, delivery,
native execution, rendering, or physical execution are not included.
