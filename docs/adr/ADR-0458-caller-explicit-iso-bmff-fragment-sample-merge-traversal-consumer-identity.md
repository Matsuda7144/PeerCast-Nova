# ADR-0458: Caller-Explicit ISO BMFF Fragment Sample Merge Traversal Consumer Identity

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Represent one caller-supplied nonempty `Guid` as an immutable opaque consumer
identity for a single-track fragment request projection. Reject missing and
empty identities at construction without resolving the identity to a runtime
object or capability.

## Non-goals

Runtime registration or lookup, consumer capability discovery, payload access,
scheduling, dispatch, delivery attempt or outcome, decoder submission, native
execution, rendering, or physical execution are not included.
