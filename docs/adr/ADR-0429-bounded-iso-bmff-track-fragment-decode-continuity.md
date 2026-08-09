# ADR-0429: Bounded ISO BMFF Track Fragment Decode Continuity

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

For two caller-selected timed fragment runs with equal track identity, compare
the previous exclusive next-decode cursor with the current base decode time.
Classify their arbitrary-precision signed delta as continuous, gap, or overlap,
and reject internally reversed run timing.

## Non-goals

Timescale conversion, fragment discovery, gap filling, overlap removal,
discontinuity reset, clock, scheduling, payload access, native execution,
rendering, or physical execution are not included.
