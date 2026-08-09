# ADR-0311: Exact ISO BMFF Movie Codec Packet Event Queue Clock Time

## Status

Accepted as a post-0.0.10e non-physical managed-demux timing slice.

## Context

ADR-0307 retains exact event bounds in seconds and ADR-0308 owns them in a
stable queue snapshot. A caller may need to express those bounds in a declared
clock rate, but choosing integral ticks or a rounding policy would prematurely
introduce scheduling semantics.

## Decision

Project every bounded queue entry into exact rational clock ticks using a
caller-explicit nonzero `uint` ticks-per-second value. Multiply each exact
seconds numerator by that rate with `BigInteger` and retain the existing
`ulong` seconds denominator unchanged. Expose exact start, exclusive end, and
duration tick numerators while retaining the exact snapshot and queue entries.

Revalidate the existing 131,072-event bound, every zero-based sequence index,
and every positive exact event interval. Fail closed for a missing snapshot,
zero tick rate, invalid sequence, or invalid event time.

No division, reduction, rounding, deadline, or clock observation occurs.

## Consequences

The queue has overflow-safe exact rational timing in any caller-declared
`uint` clock rate without losing source identity or choosing scheduler policy.

## Non-goals

Integral-tick rounding, clock origin, deadline creation, delivery outcome,
dispatch, scheduling, pacing, lateness policy, multi-sample traversal, native
packet submission, decoder execution, production MP4 demux composition,
fragmented MP4, streaming, rendering, and physical execution are not included.
