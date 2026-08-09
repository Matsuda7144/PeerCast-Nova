# ADR-0442: Exact ISO BMFF Fragment Sample Merge Traversal Time Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Project exact rational decode start, decode end, presentation, and duration
timing for every fragment traversal acknowledgement. Retain each existing
arbitrary-precision numerator and the nonzero fragment media timescale as the
common seconds denominator. Require the chain timescale and each sample
timescale to agree, require decode end minus decode start to equal duration, and
retain the exact acknowledgement source.

## Non-goals

Rounding, integral conversion, clock acquisition, deadline creation,
scheduling, delivery attempt or outcome, payload access, decoder submission,
native execution, rendering, or physical execution are not included.
