# ADR-0430: Bounded ISO BMFF Fragment Continuity Binding

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Bind one movie-fragment sequence-continuity observation and one track-fragment
decode-continuity observation into immutable two-dimensional transition
metadata. Classify the transition as fully continuous, sequence-discontinuous,
decode-discontinuous, or discontinuous in both dimensions.

## Non-goals

Fragment discovery or identity inference, policy selection, reset, clock,
scheduling, payload access, traversal, native execution, rendering, or physical
execution are not included.
