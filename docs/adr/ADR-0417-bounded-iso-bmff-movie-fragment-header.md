# ADR-0417: Bounded ISO BMFF Movie Fragment Header

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Parse one complete `mfhd` box with existing normal, extended-size, or
extends-to-end headers. Require exact payload length, FullBox version zero, and
zero flags, and retain the full unsigned sequence number without inferring
cross-fragment ordering from one isolated header.

## Non-goals

`moof` traversal, sequence continuity policy, `traf`, `tfhd`, `tfdt`, `trun`,
sample-default or data-offset resolution, streaming, decoder submission, native
execution, rendering, or physical execution are not included.
