# ADR-0354: Caller-Explicit ISO BMFF Completed Merge-Event Traversal Clock Time

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0353 owns exact rational seconds but intentionally selects no clock rate.
Callers need declarative conversion to their own tick domain without rounding or
reading a clock.

## Decision

Require a nonzero caller-explicit unsigned ticks-per-second rate. Retain each
exact ADR-0353 time by reference and multiply its start and exclusive-end
numerators by the rate using `BigInteger`; retain the exact seconds denominator
as the tick denominator. Validate exact source ordering and every nonnegative
nonempty interval.

Return no partial projection for missing input, excessive count, invalid rate,
source mismatch, or invalid time.

## Consequences

Every completed traversal event has overflow-safe exact rational clock ticks at
one caller-selected rate while remaining independent of any running clock.

## Non-goals

Integral rounding, clock origin, clock acquisition, deadline observation,
scheduling, consumption, delivery, native packet submission, decoder execution,
rendering, or physical execution are not included.
