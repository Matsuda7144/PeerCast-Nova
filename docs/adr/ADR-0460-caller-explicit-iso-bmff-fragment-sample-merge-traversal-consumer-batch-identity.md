# ADR-0460: Caller-Explicit ISO BMFF Fragment Sample Merge Traversal Consumer Batch Identity

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Represent one caller-supplied nonempty `Guid` as an immutable opaque batch
identity for exact consumer bindings. Reject missing and empty identifiers at
construction without assigning time, runtime ownership, or delivery meaning.

## Non-goals

Scheduling, runtime registration or lookup, capability resolution, payload
access, dispatch, delivery attempt or outcome, invocation, native execution,
rendering, or physical execution are not included.
