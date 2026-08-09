# ADR-0366: Explicit ISO BMFF Completed Merge-Event Traversal Dispatch Runtime Target Registry

## Status

Accepted as a post-0.0.10e non-physical managed metadata slice.

## Decision

Own one or two caller-declared immutable runtime-target registrations. Each
registration maps a nonempty ADR-0361 target ID to a nonempty opaque runtime
instance ID, explicit video or audio media kind, and positive generation.
Reject duplicate target IDs, runtime instance IDs, or media kinds and retain no
runtime object.

## Non-goals

Runtime object ownership or discovery, loader access, invocation, delivery,
native execution, rendering, or physical execution are not included.
