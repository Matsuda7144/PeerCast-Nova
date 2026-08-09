# ADR-0401: Caller-Explicit ISO BMFF Projected Completed-Event Traversal Clock Origin

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply one caller-supplied signed `BigInteger` origin tick to every exact
ADR-0400 integral interval. Retain the complete projected source and origin,
preserve duration, and expose immutable start and exclusive-end timeline ticks
without reading a clock.

## Non-goals

Clock acquisition, deadline observation, eligibility, delivery, scheduling,
runtime access, native execution, rendering, or physical execution are not
included.
