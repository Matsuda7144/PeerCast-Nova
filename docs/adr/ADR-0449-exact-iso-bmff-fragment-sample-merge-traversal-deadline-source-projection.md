# ADR-0449: Exact ISO BMFF Fragment Sample Merge Traversal Deadline Source Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Bind one exact clock-origin source projection to decode and presentation
deadline metadata rebuilt from that source. Require exact clock-origin identity
and propagate interval validation failures without accepting caller-supplied
deadline values.

## Non-goals

Current-time observation, deadline-state classification, scheduling, delivery
attempt or outcome, payload access, runtime lookup or invocation, native
execution, rendering, or physical execution are not included.
