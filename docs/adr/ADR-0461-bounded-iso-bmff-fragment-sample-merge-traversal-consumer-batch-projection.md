# ADR-0461: Bounded ISO BMFF Fragment Sample Merge Traversal Consumer Batch Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Project one exact bounded consumer-binding list and one exact caller-created
batch identity into immutable, contiguous, source-ordered batch entries. Retain
each binding by reference and require it to bind the matching exact request and
the projection's exact consumer identity.

## Non-goals

Scheduling, runtime lookup, capability access, payload access, dispatch,
delivery attempt or outcome, invocation, native execution, rendering, or
physical execution are not included.
