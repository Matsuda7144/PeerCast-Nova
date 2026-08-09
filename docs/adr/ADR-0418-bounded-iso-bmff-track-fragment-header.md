# ADR-0418: Bounded ISO BMFF Track Fragment Header

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Parse one complete `tfhd` box with existing normal, extended-size, or
extends-to-end headers. Require FullBox version zero, a nonzero track identity,
only defined flags, and exact flag-derived payload length. Preserve optional
base-data-offset, sample-description, duration, size, and sample-flags values as
nullable unsigned fields, including explicitly present zero values. Preserve
`duration-is-empty` and `default-base-is-moof` independently without resolving
their downstream meaning.

## Non-goals

`traf` traversal, `trex` inheritance, default precedence, base or data-offset
resolution, sample-run composition, media-data lookup, streaming, decoder
submission, native execution, rendering, or physical execution are not
included.
