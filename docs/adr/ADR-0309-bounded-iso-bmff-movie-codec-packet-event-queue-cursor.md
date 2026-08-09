# ADR-0309: Bounded ISO BMFF Movie Codec Packet Event Queue Cursor

## Status

Accepted as a post-0.0.10e non-physical managed-demux consumption slice.

## Context

ADR-0308 owns an immutable sequence of exact packet-event times but exposes no
way to represent bounded progress through that sequence. Progress must not
mutate the snapshot or imply delivery or scheduling.

## Decision

Create an immutable cursor at sequence index zero after revalidating the
existing 131,072-event bound and every queue entry's exact index. Advancing a
valid non-exhausted cursor returns the exact next entry and a new cursor for the
following index. The source cursor and queue snapshot remain unchanged.

Report exhaustion without an entry or successor cursor. Fail closed for a
missing snapshot or cursor, an excessive snapshot, an invalid sequence, or a
cursor position outside the closed interval from zero through the snapshot
count.

Cursor advance is a pure managed metadata operation. It does not mark an event
delivered or authorize any side effect.

## Consequences

The immutable event queue can be traversed deterministically with explicit
remaining-count and exhaustion facts while preserving source identity.

## Non-goals

Delivery acknowledgement, mutable queue state, clock-tick conversion,
scheduling, pacing, lateness policy, multi-sample traversal, native packet
submission, decoder execution, production MP4 demux composition, fragmented
MP4, streaming, rendering, and physical execution are not included.
