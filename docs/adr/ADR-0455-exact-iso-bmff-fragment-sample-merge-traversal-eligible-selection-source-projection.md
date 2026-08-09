# ADR-0455: Exact ISO BMFF Fragment Sample Merge Traversal Eligible Selection Source Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Bind one exact deadline-eligibility source projection to the selection rebuilt
under the caller's exact combination policy and candidate limit. Require exact
eligibility-source identity, policy, and limit, and propagate validation
failures without accepting caller-supplied candidates.

## Non-goals

Default policy, priority reordering, scheduling, dispatch, delivery attempt or
outcome, payload access, runtime lookup or invocation, native execution,
rendering, or physical execution are not included.
