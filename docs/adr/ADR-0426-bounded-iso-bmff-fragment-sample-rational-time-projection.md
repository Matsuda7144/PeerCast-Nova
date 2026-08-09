# ADR-0426: Bounded ISO BMFF Fragment Sample Rational-Time Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Bind one caller-provided nonzero media timescale to one fragment sample metadata
projection. Preserve decode start, decode exclusive end, presentation time, and
duration as exact arbitrary-precision numerators over the unchanged unsigned
media-timescale denominator while retaining sample identity, flags, size, and
byte-range metadata.

## Non-goals

Rounding or `TimeSpan` conversion, clock acquisition, cross-track ordering,
cross-fragment continuity, `mdat` admission, payload views or ownership, decoder
packet composition, traversal, streaming, native execution, rendering, or
physical execution are not included.
