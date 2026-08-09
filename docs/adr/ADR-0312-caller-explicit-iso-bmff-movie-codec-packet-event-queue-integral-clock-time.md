# ADR-0312: Caller-Explicit ISO BMFF Movie Codec Packet Event Queue Integral Clock Time

## Status

Accepted as a post-0.0.10e non-physical managed-demux timing slice.

## Context

ADR-0311 retains exact rational clock ticks without selecting integral values.
Some later managed composition will require integral ticks, but the repository
must not silently choose a rounding policy or clock origin.

## Decision

Require callers to state `Floor`, `Ceiling`, or `RequireExact` independently
for event starts and exclusive ends. Divide the ADR-0311 `BigInteger` numerator
by its retained denominator and apply only the stated mode. Retain the exact
ADR-0311 time object and expose integral start, exclusive end, and duration as
`BigInteger`.

Reject a missing source, more than the existing 131,072-event bound, undefined
rounding modes, fractional input under `RequireExact`, invalid source time, or
any rounding combination that produces an empty or reversed interval.

There is no default rounding mode. The projection does not attach a clock
origin, create a deadline, observe a clock, or authorize dispatch.

## Consequences

Callers can obtain bounded integral clock intervals under an explicit policy
without overflow or hidden rounding decisions.

## Non-goals

Default rounding policy, clock origin, deadline creation, delivery outcome,
dispatch, scheduling, pacing, lateness policy, multi-sample traversal, native
packet submission, decoder execution, production MP4 demux composition,
fragmented MP4, streaming, rendering, and physical execution are not included.
