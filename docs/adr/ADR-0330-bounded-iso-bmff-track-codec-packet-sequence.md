# ADR-0330: Bounded ISO BMFF Track Codec Packet Sequence

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0300 through ADR-0302 bind one current codec packet to exact track and movie
sources. Multi-sample work needs an immutable per-track sequence boundary before
cursor traversal or automatic sample composition is considered.

## Decision

Own between one and 131,072 caller-provided movie-track codec packets in exact
caller order. Require every packet to retain the same movie timing, track
identity, media timing, and handler objects. Require nonzero strictly increasing
sample numbers, and assign deterministic zero-based sequence indexes.

Copy the caller list into a read-only collection while retaining the exact
packet objects and shared sources. Fail closed for missing or excessive inputs,
missing entries, foreign sources, invalid sample zero, duplicates, or decreasing
sample numbers.

## Consequences

Already composed packets for one exact track can be represented as a bounded
multi-sample sequence without losing sample or source identity.

## Non-goals

No sample-table iteration, automatic packet construction, file or `mdat` read,
cursor traversal, cross-track merge, event projection, scheduling, native packet
submission, decoder execution, rendering, or physical execution is included.
