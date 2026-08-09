# ADR-0370: Bounded ISO BMFF Completed Merge-Event Traversal Dispatch Admitted Invocation Selection

## Status

Accepted as a post-0.0.10e non-physical managed metadata slice.

## Decision

Select admitted ADR-0369 entries only, in exact source order, under one
positive caller maximum bounded by the completed-event limit. Retain the exact
admission objects, maximum, total admitted count before truncation, and an
explicit truncation indicator. Reject foreign ordering or invalid dispositions
without a partial selection.

Selection is immutable metadata and does not make any selected entry
executable.

## Non-goals

Runtime object access, authorization packets, invocation, dispatch, delivery,
consumption, native execution, rendering, or physical execution are not
included.
