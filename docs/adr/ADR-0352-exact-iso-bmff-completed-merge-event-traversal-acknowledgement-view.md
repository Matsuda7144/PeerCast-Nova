# ADR-0352: Exact ISO BMFF Completed Merge-Event Traversal Acknowledgement View

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0351 certifies cumulative coverage across an initial ADR-0349 traversal and
optional ADR-0350 continuation chain, while each segment owns its own exact
acknowledgements.

## Decision

Walk the initial traversal and continuation chain iteratively. Retain every
exact ADR-0348 acknowledgement behind a wrapper with one contiguous zero-based
global traversal index. Require exact source/successor cursor chaining within
and across segments, exact absolute event indexes, the certified total count,
and the exact certified final cursor.

Accept a certified already-exhausted zero-event traversal as an empty view.
Reject missing completion, unsupported source implementations, invalid chains,
or count mismatch without returning a partial view.

## Consequences

Every transition in one certified completed event traversal can be addressed in
global traversal order without copying events or acknowledgements.

## Non-goals

Consumption, delivery, scheduling, native packet submission, decoder execution,
rendering, or physical execution are not included.
