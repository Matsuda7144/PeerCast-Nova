# ADR-0416: Bounded ISO BMFF Track Extends Box

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Parse one complete `trex` box with existing normal, extended-size, or
extends-to-end box headers. Require exact payload length, FullBox version zero,
zero flags, a nonzero track ID, and a nonzero default sample-description index.
Project default sample duration, size, and flags exactly; zero duration and size
remain valid fragment-level override defaults.

## Non-goals

`mvex` traversal, `moof`, `traf`, `tfhd`, `tfdt`, `trun`, sample-default
resolution, data-offset resolution, fragmented media-data lookup, streaming,
decoder submission, native execution, rendering, or physical execution are not
included.
