# ADR-0445: Exact ISO BMFF Fragment Sample Merge Traversal Integral Time Source Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Bind one exact rational-time source projection to integral decode and
presentation intervals produced with the caller's tick rate and rounding modes.
Require exact rational source identity, tick rate, and rounding modes, and
propagate conversion failures without accepting caller-supplied interval data.

## Non-goals

Default policy, clock acquisition, clock origin, deadline creation, scheduling,
delivery attempt or outcome, payload access, runtime lookup or invocation,
native execution, rendering, or physical execution are not included.
