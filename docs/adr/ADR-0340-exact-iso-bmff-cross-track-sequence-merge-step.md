# ADR-0340: Exact ISO BMFF Cross-Track Sequence Merge Step

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0339 owns one exact selected cursor transition and its successor cursor set.
A later bounded merge needs one source-bound output step before it can own or
repeat a complete traversal.

## Decision

Require a caller-provided zero-based merge index below 262,144, the sum of the
two selected track-sequence packet limits. Retain the exact ADR-0339 advance,
selected indexed stream, entry, packet, leading appearance, and successor cursor
set. Expose exact role, stream index, track ID, sequence index, and sample number.

Revalidate the source selection, selected source/entry/successor transition,
selected sequence, leading appearance, and reference-identical non-selected
cursor. Reject a missing advance, invalid merge index, or inconsistent advance
without returning a partial step.

## Consequences

One exact cross-track output step has deterministic source and successor-state
provenance without mutating cursors or owning a repeated merge.

## Non-goals

Automatic merge-index assignment, repeated traversal, merged-list ownership,
consumption or delivery, repeated appearance traversal, event projection,
scheduling, automatic packet construction, file or `mdat` reads, native packet
submission, decoder execution, rendering, or physical execution are included.
