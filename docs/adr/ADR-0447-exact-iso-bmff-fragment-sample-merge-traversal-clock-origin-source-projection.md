# ADR-0447: Exact ISO BMFF Fragment Sample Merge Traversal Clock Origin Source Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Bind one exact integral-time source projection to the caller-origin timeline
projection rebuilt from it. Require exact integral source identity and origin
value, and propagate interval validation failures without accepting
caller-supplied timeline intervals.

## Non-goals

Clock acquisition or observation, default origin selection, deadline creation,
scheduling, delivery attempt or outcome, payload access, runtime lookup or
invocation, native execution, rendering, or physical execution are not
included.
