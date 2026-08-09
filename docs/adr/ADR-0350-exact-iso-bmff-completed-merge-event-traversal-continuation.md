# ADR-0350: Exact ISO BMFF Completed Merge-Event Traversal Continuation

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0349 intentionally truncates at a caller bound. A later caller needs to
continue from the exact final cursor without re-emitting prior transitions or
losing cumulative count provenance.

## Decision

Continue only a valid non-exhausted traversal source from its exact final cursor.
Require a positive additional bound within the remaining 131,072-event global
limit. Retain the source and new ADR-0349 segment by reference, expose the
cumulative starting index, segment count, total count, and final exhaustion.

Require exact projection identity and equality between initial index plus total
count and final index. Reject an empty continuation segment and return no
partial continuation.

## Consequences

Finite segments can form an exact cumulative event-transition chain without
copying or re-emitting earlier acknowledgements.

## Non-goals

Completion certification, consumption, delivery, scheduling, native packet
submission, decoder execution, rendering, or physical execution are not
included.
