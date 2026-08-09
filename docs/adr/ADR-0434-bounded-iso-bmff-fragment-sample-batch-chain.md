# ADR-0434: Bounded ISO BMFF Fragment Sample Batch Chain

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Bind a caller-provided fragment sample batch list to one validated continuity
chain with a default batch limit of 4,096 and hard limit of 65,536. Require zero
batches with zero transitions, or exactly one fewer transition than batches.
Require stable track identity and media timescale plus exact sequence and full
timed-run endpoint agreement for every adjacent transition. Preserve sample
description changes and discontinuity classifications.

## Non-goals

Fragment discovery or traversal, timescale repair, discontinuity reset, sample
merge, payload access, clock, scheduling, streaming, native execution,
rendering, or physical execution are not included.
