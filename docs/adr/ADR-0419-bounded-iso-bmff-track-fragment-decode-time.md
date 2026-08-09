# ADR-0419: Bounded ISO BMFF Track Fragment Decode Time

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Parse one complete `tfdt` box with existing normal, extended-size, or
extends-to-end headers. Require zero flags and exact version-derived payload
length. Preserve version-zero base media decode time as an unsigned 32-bit
value and version-one time as an unsigned 64-bit value in one lossless
projection.

## Non-goals

Track-timescale conversion, timeline continuity, fragment ordering, `traf`
traversal, run composition, media-data lookup, streaming, decoder submission,
native execution, rendering, or physical execution are not included.
