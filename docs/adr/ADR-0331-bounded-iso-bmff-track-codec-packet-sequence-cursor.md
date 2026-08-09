# ADR-0331: Bounded ISO BMFF Track Codec Packet Sequence Cursor

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0330 owns an immutable same-track packet sequence. Traversal needs a pure
cursor that cannot mutate the sequence or imply that a returned packet was
delivered.

## Decision

Create an immutable cursor at sequence index zero after revalidating the
131,072-packet bound, deterministic indexes, and strictly increasing nonzero
sample numbers. Expose exact remaining count and exhaustion state.

Advance one entry at a time by returning the exact sequence entry and a new
cursor over the same sequence. Report exhaustion without an entry or successor.
Fail closed for missing inputs, out-of-range cursor positions, or inconsistent
sequence indexes.

## Consequences

Caller-provided multi-sample packets can be traversed deterministically without
mutating source state or claiming consumption.

## Non-goals

No acknowledgement, delivery claim, automatic packet construction, file or
`mdat` read, cross-track merge, event projection, scheduling, native packet
submission, decoder execution, rendering, or physical execution is included.
