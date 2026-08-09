# ADR-0365: Exact ISO BMFF Completed Merge-Event Traversal Dispatch Target Resolution

## Status

Accepted as a post-0.0.10e non-physical managed metadata slice.

## Decision

Resolve every exact ADR-0363 request, in source order, against the caller-owned
ADR-0364 capability with the same opaque target ID. Retain each request and
capability by reference, require exact binding order, matching media kind, and
a payload length within the declared maximum, and fail without a partial
projection when any requirement is not met.

## Non-goals

Runtime registration or lookup, invocation, admission policy, delivery,
consumption, native execution, rendering, or physical execution are not
included.
