# ADR-0425: Bounded ISO BMFF Fragment Sample Metadata Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Join one timed fragment run and one sample byte-range projection only when track
identity, sample-description identity, sample count, exact source index, and
sample size agree. Preserve duration, size, flags, exact decode/presentation
times, and arbitrary-precision byte offsets in one immutable ordered metadata
projection.

## Non-goals

Timescale conversion, `mdat` admission, payload views or ownership, codec sample
entry selection, decoder packet composition, fragment traversal, streaming,
native execution, rendering, or physical execution are not included.
