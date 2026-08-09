# ADR-0345: Exact ISO BMFF Completed Merge-Step View

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0344 certifies packet completion across an initial traversal and optional
continuation chain, while each retained segment keeps local step indexes.
Presentation expansion needs one ordered global view without copying packets or
re-emitting steps into a new merge traversal.

## Decision

Walk the exact ADR-0342 initial traversal and ADR-0343 continuation chain
iteratively. Retain each exact ADR-0340 step behind a lightweight wrapper with a
contiguous zero-based global merge index. Require one exact policy, exact cursor
continuity within and across segments, exact completion count, and the exact
certified final cursor state.

Accept a zero-step already-exhausted completion. Fail closed for a missing
completion, unsupported traversal-source implementation, count mismatch, or
invalid step chain without returning a partial view.

## Consequences

Every packet in one certified merge can be addressed in global order while
retaining exact segment-local step and cursor provenance.

## Non-goals

Copying packet payloads, expanding edit appearances, presentation-event order,
consumption or delivery, scheduling, native packet submission, decoder
execution, rendering, or physical execution are not included.
