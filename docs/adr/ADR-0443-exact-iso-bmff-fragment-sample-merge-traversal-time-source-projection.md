# ADR-0443: Exact ISO BMFF Fragment Sample Merge Traversal Time Source Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Bind the exact acknowledgement-view source projection to the rational fragment
sample timing rebuilt from that view. Require exact view identity and sample
count, and propagate bounded-timescale and exact-time validation failures
without accepting caller-supplied timing records.

## Non-goals

Rounding, integral conversion, clock acquisition, deadline creation,
scheduling, delivery attempt or outcome, payload access, runtime lookup or
invocation, native execution, rendering, or physical execution are not
included.
