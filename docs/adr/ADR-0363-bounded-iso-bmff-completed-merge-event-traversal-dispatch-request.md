# ADR-0363: Bounded ISO BMFF Completed Merge-Event Traversal Dispatch Request

## Status

Accepted as a post-0.0.10e non-physical managed metadata slice.

## Decision

Project each exact ADR-0362 binding into immutable target ID, media kind, codec
packet, nonempty payload, and deadline metadata. Retain the binding by reference
and require exact source order and target identity without storing an invocable
runtime object.

## Non-goals

Capability resolution, runtime lookup, invocation, delivery, native execution,
rendering, or physical execution are not included.
