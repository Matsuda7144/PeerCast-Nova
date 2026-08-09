# ADR-0439: Exact ISO BMFF Fragment Sample Merge Traversal Completion

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Project immutable completion metadata only from an exhaustive fragment sample
merge traversal. Revalidate the exact merge identity, initial and final indexes,
remaining-step count, every sample, and every contiguous cursor identity before
retaining the source traversal, total sample count, and traversed sample count.

## Non-goals

Acknowledgement, delivery outcome, cursor mutation, clock acquisition,
scheduling, discontinuity action, `mdat` or payload access, decoder packet
composition, native execution, rendering, or physical execution are not
included.
