# ADR-0335: Bounded ISO BMFF Selected Track Codec Packet Sequence Cursor Set

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0334 retains explicit video and/or audio sequence roles. Cross-track work
needs immutable initial cursors for exactly those roles before packet heads can
be inspected or compared.

## Decision

Retain the exact ADR-0334 selection and create an ADR-0331 cursor at sequence
index zero for each selected role. Preserve absent video or audio roles as
absent cursors and permit either explicit single-media selection.

Reject a missing selection or an internally empty selection. Reuse ADR-0331
validation for each selected sequence. If either selected cursor cannot be
created, fail the whole operation without returning a partial cursor set.

## Consequences

The exact selected video and/or audio sequences have immutable initial traversal
positions without mutating the source selection or implying consumption.

## Non-goals

Cursor advance, acknowledgement, packet-head inspection or comparison,
cross-track merge, event projection, delivery, scheduling, automatic packet
construction, file or `mdat` reads, native packet submission, decoder execution,
rendering, or physical execution are included.
