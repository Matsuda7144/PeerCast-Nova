# ADR-0361: Explicit ISO BMFF Completed Merge-Event Traversal Dispatch Target Set

## Status

Accepted as a post-0.0.10e non-physical managed metadata slice.

## Decision

Own caller-explicit nonempty opaque `Guid` identities for video and/or audio.
Require distinct IDs and retain media kind only; no runtime instance or
invocable target is stored.

## Non-goals

Binding, runtime lookup, dispatch, delivery, native execution, rendering, or
physical execution are not included.
