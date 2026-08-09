# ADR-0368: Caller-Identified ISO BMFF Completed Merge-Event Traversal Dispatch Invocation Intent

## Status

Accepted as a post-0.0.10e non-physical managed metadata slice.

## Decision

Project every exact ADR-0367 lookup, in source order, into immutable invocation
intent metadata under one nonempty caller-supplied invocation ID. Retain each
lookup by reference and expose its exact request, opaque runtime instance ID,
positive generation, and zero-based sequence index without accessing or
invoking a runtime object.

## Non-goals

Runtime object access, loader access, invocation, admission policy, delivery,
consumption, native execution, rendering, or physical execution are not
included.
