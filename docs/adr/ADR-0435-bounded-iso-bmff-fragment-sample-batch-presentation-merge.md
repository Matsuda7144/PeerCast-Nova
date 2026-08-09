# ADR-0435: Bounded ISO BMFF Fragment Sample Batch Presentation Merge

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Merge all ordered samples from one validated batch chain with a default sample
limit of 4,096 and hard limit of 65,536. Require each sample timescale to match
the chain and sort by exact presentation numerator, exact decode numerator,
caller batch index, then source sample index. Preserve batch index, fragment
sequence number, source sample metadata, and the complete batch chain.

## Non-goals

Cursor mutation, acknowledgement, clock conversion, scheduling, discontinuity
handling, `mdat` or payload access, decoder packet composition, traversal,
streaming, native execution, rendering, or physical execution are not included.
