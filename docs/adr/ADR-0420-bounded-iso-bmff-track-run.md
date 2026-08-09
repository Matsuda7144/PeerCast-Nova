# ADR-0420: Bounded ISO BMFF Track Run

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Parse one complete `trun` box with existing normal, extended-size, or
extends-to-end headers. Accept FullBox versions zero and one, only defined
flags, exact flag/count-derived payload length, and a configurable entry limit
of 4,096 by default with a hard maximum of 65,536. Preserve signed data offset,
optional first-sample flags, and nullable per-entry duration, size, flags, and
composition offset. Interpret composition offsets as unsigned for version zero
and signed for version one. Reject simultaneous first-sample and per-sample
flags.

## Non-goals

`trex`/`tfhd` default resolution, absolute data-offset calculation, sample byte
range lookup, decode/presentation timestamp accumulation, `traf` traversal,
streaming, decoder submission, native execution, rendering, or physical
execution are not included.
