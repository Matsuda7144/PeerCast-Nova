# ADR-0307: Exact ISO BMFF Movie Codec Packet Event Time

## Status

Accepted as a post-0.0.10e non-physical managed-demux metadata slice.

## Context

ADR-0306 retains packet-event bounds as exact rational movie-time values in a
deterministic cross-stream order. A later managed demux composition needs those
bounds expressed in seconds without losing their exact source timing.

## Decision

Project every ordered event into exact rational seconds. Retain the exact
ADR-0306 order and event object. Keep each movie-time numerator unchanged and
multiply the event denominator by the exact `mvhd` timescale. The product is
stored as `ulong`, which exactly contains the product of the two valid `uint`
factors. Expose exact start, exclusive end, and duration numerators without
rounding or reduction.

Reject a missing order, more than the existing 131,072-event bound, a zero
movie timescale, an event whose movie timescale differs from the retained
`mvhd`, or an invalid rational interval.

This projection is managed metadata. It does not choose a clock unit, create a
deadline, or authorize event delivery.

## Consequences

The current ordered video and audio packet events can be interpreted in one
exact seconds domain while retaining their source objects and ordering.

## Non-goals

Clock-tick conversion, rational reduction, queue ownership, scheduling, pacing,
lateness policy, multi-sample traversal, native packet submission, decoder
execution, production MP4 demux composition, fragmented MP4, streaming,
rendering, and physical execution are not included.
