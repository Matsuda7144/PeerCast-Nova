# ADR-0453: Exact ISO BMFF Fragment Sample Merge Traversal Deadline Eligibility Source Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Bind one exact deadline-observation source projection to eligibility metadata
rebuilt under the caller's exact decode and presentation policies. Require exact
observation-source and policy-object identity and propagate policy validation
failures without accepting caller-supplied eligibility facts.

## Non-goals

Default policy, candidate selection, scheduling, dispatch, delivery attempt or
outcome, payload access, runtime lookup or invocation, native execution,
rendering, or physical execution are not included.
