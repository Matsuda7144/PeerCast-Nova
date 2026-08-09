# ADR-0346: Bounded ISO BMFF Completed Merge-Event Projection

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0345 provides one exact global step view over every packet in an ADR-0344
certified merge. A packet can have more than one complete edit appearance, so
the leading appearance used to choose merge steps is insufficient for complete
presentation-event metadata.

## Decision

Require a positive caller event limit no greater than 131,072. Count every edit
appearance for every exact ADR-0345 step before allocating output, reject a
missing appearance or a total above the caller limit, and return no partial
projection.

Retain each exact completed step, zero-based appearance index, and complete edit
appearance in one immutable event. Validate the exact packet media timescale,
nonnegative nonempty interval, and complete-sample flag. Order the complete
event set by exact rational start, exact rational exclusive end, global merge
index, and appearance index. Use arbitrary-precision cross-products only for
comparison so `Int128` operands cannot overflow. An exact zero-step completed
view projects to an empty event set.

## Consequences

Every complete edit appearance from every packet in one certified merge is
owned once in a deterministic bounded presentation order with exact packet,
step, and appearance provenance.

## Non-goals

Event cursor traversal, acknowledgement, consumption or delivery, scheduling,
clock acquisition, native packet submission, decoder execution, rendering, or
physical execution are not included.
