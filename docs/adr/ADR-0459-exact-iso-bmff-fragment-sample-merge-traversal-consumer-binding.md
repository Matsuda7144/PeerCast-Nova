# ADR-0459: Exact ISO BMFF Fragment Sample Merge Traversal Consumer Binding

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Bind every request from one exact request-source projection to one exact
caller-created opaque consumer identity. Preserve request order and reference
identity, require the request projection to belong to its exact selection
source, require contiguous request indexes and exact candidate references, and
require every request to retain the projection's nonzero track ID.

## Non-goals

Runtime lookup, capability resolution, payload access, scheduling, dispatch,
delivery attempt or outcome, invocation, native execution, rendering, or
physical execution are not included.
