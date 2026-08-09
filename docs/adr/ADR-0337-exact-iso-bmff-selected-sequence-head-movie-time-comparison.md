# ADR-0337: Exact ISO BMFF Selected Sequence Head Movie-Time Comparison

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0336 exposes exact current video and/or audio sequence entries without
advancing them. Cross-track ordering needs an exact time relation before a
separate policy may choose which role advances.

## Decision

For each current head, retain its exact first complete movie appearance as the
leading interval. Compare video and audio leading starts as exact rationals;
when starts are equal, compare exact rational ends. Use `BigInteger` cross
multiplication so valid `Int128` numerators cannot overflow.

Report video-before-audio, the same movie interval, audio-before-video,
video-only, audio-only, or whole-set exhaustion. Retain the exact ADR-0336
snapshot and exact leading appearances. Validate the head-to-cursor identity
and leading complete-appearance interval. Fail closed for a missing snapshot or
an invalid role head.

This comparison does not apply a stream-index tie-break and does not choose a
role to advance.

## Consequences

Callers can observe an exact cross-timescale relation between current selected
packet heads without rounding, cursor mutation, or an implicit merge policy.

## Non-goals

Winner selection, stream-index tie-breaking, cursor advance, acknowledgement,
cross-track merge, repeated appearance traversal, event projection, delivery,
scheduling, automatic packet construction, file or `mdat` reads, native packet
submission, decoder execution, rendering, or physical execution are included.
