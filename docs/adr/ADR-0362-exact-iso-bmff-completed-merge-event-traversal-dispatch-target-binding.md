# ADR-0362: Exact ISO BMFF Completed Merge-Event Traversal Dispatch Target Binding

## Status

Accepted as a post-0.0.10e non-physical managed metadata slice.

## Decision

Bind each exact eligible ADR-0360 candidate by its packet media kind to the
corresponding exact ADR-0361 opaque target. Retain candidate and target by
reference and fail atomically for source mismatch or missing/mismatched media.

## Non-goals

Capabilities, runtime lookup, dispatch, delivery, native execution, rendering,
or physical execution are not included.
