# ADR-0451: Exact ISO BMFF Fragment Sample Merge Traversal Deadline Observation Source Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Bind one exact deadline source projection to the observation snapshot rebuilt at
the caller's exact tick. Require exact deadline-source identity and observation
tick, and propagate deadline validation failures without accepting caller-
supplied state classifications.

## Non-goals

Clock acquisition, repeated observation, eligibility policy, scheduling,
delivery attempt or outcome, payload access, runtime lookup or invocation,
native execution, rendering, or physical execution are not included.
