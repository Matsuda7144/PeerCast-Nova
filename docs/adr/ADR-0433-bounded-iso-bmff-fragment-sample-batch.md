# ADR-0433: Bounded ISO BMFF Fragment Sample Batch

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Bind one caller-selected `mfhd` header, one timed run, and one rational-time
presentation order only when track identity, sample-description identity,
sample count, source index, timing, size, flags, timescale, and ordered
permutation agree exactly. Preserve all three source objects in one immutable
fragment batch.

## Non-goals

Fragment discovery or traversal, continuity inference, `mdat` or payload access,
clock, scheduling, decoder packet composition, streaming, native execution,
rendering, or physical execution are not included.
