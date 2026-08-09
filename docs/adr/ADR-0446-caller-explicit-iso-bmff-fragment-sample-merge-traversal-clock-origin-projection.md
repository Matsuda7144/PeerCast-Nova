# ADR-0446: Caller-Explicit ISO BMFF Fragment Sample Merge Traversal Clock Origin Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Add one caller-explicit signed arbitrary-precision origin tick to every decode
and presentation interval in an integral fragment traversal timing projection.
Retain the exact integral source, validate both interval chains, and expose
origin-shifted half-open timeline intervals without reading a clock.

## Non-goals

Clock acquisition or observation, default origin selection, deadline creation,
scheduling, delivery attempt or outcome, payload access, decoder submission,
native execution, rendering, or physical execution are not included.
