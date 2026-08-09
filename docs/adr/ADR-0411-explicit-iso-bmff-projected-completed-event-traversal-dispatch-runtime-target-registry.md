# ADR-0411: Explicit ISO BMFF Projected Completed-Event Traversal Dispatch Runtime-Target Registry

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Attach at most two caller-declared opaque target, runtime-instance, media-kind,
and positive-generation identities to one exact ADR-0410 resolution projection.
The immutable registry contains identifiers only and retains the complete
projected source.

## Non-goals

Runtime object references, instance access, target calls, request submission,
delivery, invocation, native execution, rendering, or physical execution are
not included.
