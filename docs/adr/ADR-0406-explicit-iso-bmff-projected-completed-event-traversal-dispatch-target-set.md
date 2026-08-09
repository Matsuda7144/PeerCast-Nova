# ADR-0406: Explicit ISO BMFF Projected Completed-Event Traversal Dispatch Target Set

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Attach a caller-supplied non-empty set of distinct opaque video and/or audio
target IDs to one exact ADR-0405 candidate selection. Retain the complete
projected source and media-kind declarations without resolving target instances.

## Non-goals

Target binding, capability discovery, registry access, delivery, invocation,
native execution, rendering, or physical execution are not included.
