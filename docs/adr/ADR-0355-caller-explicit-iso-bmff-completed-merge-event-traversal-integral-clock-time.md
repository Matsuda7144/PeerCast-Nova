# ADR-0355: Caller-Explicit ISO BMFF Completed Merge-Event Traversal Integral Clock Time

## Status

Accepted as a post-0.0.10e non-physical managed metadata slice.

## Decision

Apply caller-explicit `Floor`, `Ceiling`, or `RequireExact` policies separately
to ADR-0354 start and exclusive-end rational ticks. Use `BigInteger` division,
reject nonintegral exact requests and empty rounded intervals, and retain every
exact clock-time source by reference without partial output.

## Non-goals

Clock origin or acquisition, delivery, scheduling, native execution, rendering,
or physical execution are not included.
