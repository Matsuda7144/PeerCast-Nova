# ADR-0351: Exact ISO BMFF Completed Merge-Event Traversal Completion

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

Exhaustion reported by ADR-0349 or ADR-0350 is not alone a proof that every
event remaining at the root cursor has exactly one retained transition.

## Decision

Certify completion only for an exhausted traversal source whose initial and
final cursors retain the same ADR-0346 projection, whose final cursor is exactly
exhausted, and whose cumulative event count equals the root cursor's initial
remaining count. Also require the final index to equal the initial index plus
the cumulative count.

Retain the exact source and initial remaining count. Fail closed for a missing
or truncated source, projection mismatch, invalid cursor state, or count
mismatch.

## Consequences

The certificate proves metadata-level traversal coverage of every event that
remained at the root cursor.

## Non-goals

Successful consumption or delivery, scheduling, native packet submission,
decoder execution, rendering, or physical execution are not proved or
included.
