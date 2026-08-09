# ADR-0339: Exact ISO BMFF Selected Sequence Cursor-Set Advance

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0338 selects one exact video or audio cursor that may advance but does not
produce a successor movie cursor state.

## Decision

Revalidate the exact ADR-0338 selection, advance only its selected ADR-0331
cursor by one entry, and retain the resulting exact ADR-0332 source/entry/
successor transition. Construct an immutable successor ADR-0335 cursor set by
replacing only the selected role cursor and retaining the non-selected cursor
by reference.

Report whole-set exhaustion without a transition. Fail closed for a missing or
inconsistent role selection, a failed cursor advance, or an invalid transition,
without returning a partial successor set.

This is a pure immutable state transition. It does not mutate the source cursor
set and does not claim packet consumption or delivery.

## Consequences

One selected packet entry and exact successor movie cursor state can be retained
with deterministic role provenance while the source state remains unchanged.

## Non-goals

Consumption or delivery acknowledgement, repeated merge traversal, bounded
merged output ownership, repeated appearance traversal, event projection,
scheduling, automatic packet construction, file or `mdat` reads, native packet
submission, decoder execution, rendering, or physical execution are included.
