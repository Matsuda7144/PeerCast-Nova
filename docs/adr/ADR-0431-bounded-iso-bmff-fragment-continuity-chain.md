# ADR-0431: Bounded ISO BMFF Fragment Continuity Chain

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Project caller-provided continuity transitions into one immutable chain with a
default limit of 4,096 and hard limit of 65,536. Require one track identity and
exact adjacency between each prior sequence/decode endpoint and the next
transition's starting endpoint. Preserve discontinuous classifications when the
endpoints themselves still chain exactly.

## Non-goals

Fragment discovery, automatic traversal, discontinuity repair or reset, clock,
scheduling, payload access, streaming, native execution, rendering, or physical
execution are not included.
