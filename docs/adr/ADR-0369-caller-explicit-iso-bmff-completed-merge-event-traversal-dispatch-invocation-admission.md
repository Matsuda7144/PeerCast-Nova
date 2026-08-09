# ADR-0369: Caller-Explicit ISO BMFF Completed Merge-Event Traversal Dispatch Invocation Admission

## Status

Accepted as a post-0.0.10e non-physical managed metadata slice.

## Decision

Project one caller-explicit admitted or denied disposition for every exact
ADR-0368 intent in source order. Retain each intent by reference and revalidate
the positional lookup, sequence index, shared invocation ID, opaque runtime
instance ID, and positive generation. Fail without a partial projection for a
missing, invalid, reordered, or count-mismatched disposition.

Admission is immutable classification metadata only. It is not an execution
authorization and does not grant runtime access.

## Non-goals

Runtime object access, authorization packets, invocation, dispatch, delivery,
consumption, native execution, rendering, or physical execution are not
included.
