# ADR-0457: Exact ISO BMFF Fragment Sample Merge Traversal Request Source Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Bind one exact eligible-selection source projection to target-free request
metadata rebuilt from that selection. Require exact selection identity and
propagate track, timescale, source-order, and byte-range validation failures
without accepting caller-supplied request records.

## Non-goals

Payload access, target or capability binding, scheduling, dispatch, delivery
attempt or outcome, runtime lookup or invocation, native execution, rendering,
or physical execution are not included.
