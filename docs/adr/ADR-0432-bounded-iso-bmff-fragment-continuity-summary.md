# ADR-0432: Bounded ISO BMFF Fragment Continuity Summary

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Summarize one validated continuity chain into transition, fully-continuous,
sequence-discontinuity, decode-gap, and decode-overlap counts. Project only a
declarative continuous or discontinuity-handling-required disposition; do not
select or perform handling.

## Non-goals

Discontinuity policy selection, reset or flush, decoder/runtime access, clock,
scheduling, payload access, traversal, streaming, rendering, or physical
execution are not included.
