# ADR-0364: Explicit ISO BMFF Completed Merge-Event Traversal Dispatch Target Capability Set

## Status

Accepted as a post-0.0.10e non-physical managed metadata slice.

## Decision

Own one or two caller-declared immutable dispatch target capabilities. Each
declaration contains a nonempty opaque target ID, an explicit video or audio
media kind, and a positive maximum payload byte count. Reject duplicate target
IDs, duplicate media kinds, invalid fields, and declaration counts beyond the
video/audio bound.

## Non-goals

Runtime registration or lookup, decoder capability discovery, invocation,
delivery, native execution, rendering, or physical execution are not included.
