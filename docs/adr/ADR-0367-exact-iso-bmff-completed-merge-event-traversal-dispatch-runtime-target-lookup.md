# ADR-0367: Exact ISO BMFF Completed Merge-Event Traversal Dispatch Runtime Target Lookup

## Status

Accepted as a post-0.0.10e non-physical managed metadata slice.

## Decision

Look up every exact ADR-0365 resolution, in source order, against the ADR-0366
registration with the same target ID. Retain each resolution and registration
by reference, require matching request and capability media kinds and a
positive generation, and fail without a partial projection when any
registration is missing or inconsistent.

## Non-goals

Runtime object access, loader access, invocation, admission policy, delivery,
consumption, native execution, rendering, or physical execution are not
included.
