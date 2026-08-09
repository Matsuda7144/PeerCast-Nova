# ADR-0306: Exact ISO BMFF Movie Codec Packet Event Order

## Status

Accepted as a post-0.0.10e non-physical managed-demux metadata slice.

## Context

ADR-0305 deliberately keeps video and audio event lists separate. A later
managed demux composition needs one deterministic cross-stream order, but
rounding either stream to a clock unit could change the order.

## Decision

Snapshot all ADR-0305 events into one read-only list ordered by exact rational
movie start time. Break equal-start ties by exact rational end time, then stream
index, then appearance index.

Compare rationals by cross multiplication with `BigInteger`, so valid `Int128`
numerators cannot overflow during comparison. Retain the exact ADR-0305
projection and exact event objects. Reject a missing projection, more than the
existing 131,072-event bound, or an invalid rational interval.

This order is a managed metadata fact. It does not convert an event to a clock
deadline or authorize its delivery.

## Consequences

Video and audio current-packet appearances now have a deterministic exact
cross-timescale sequence without loss from timestamp normalization.

## Non-goals

Timestamp conversion, queue ownership, scheduling, pacing, lateness policy,
multi-sample traversal, native packet submission, decoder execution, production
MP4 demux composition, fragmented MP4, streaming, rendering, and physical
execution are not included.
