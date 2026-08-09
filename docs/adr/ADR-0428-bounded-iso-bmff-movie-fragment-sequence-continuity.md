# ADR-0428: Bounded ISO BMFF Movie Fragment Sequence Continuity

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Classify two caller-selected `mfhd` sequence numbers as sequential,
maximum-to-zero wrapped sequential, duplicate, or otherwise discontinuous.
Preserve the expected unsigned next value and exact signed raw delta without
inferring ordering from unrelated fragments.

## Non-goals

Fragment discovery, traversal, retry, sequence repair, decode continuity, clock,
scheduling, payload access, native execution, rendering, or physical execution
are not included.
