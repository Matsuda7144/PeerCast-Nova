# ADR-0399: Caller-Explicit ISO BMFF Projected Completed-Event Traversal Clock Time

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply one positive caller-supplied ticks-per-second rate to the exact ADR-0398
rational seconds projection. Retain the complete projected source and represent
tick numerators with `BigInteger` while preserving each exact denominator and
event identity.

## Non-goals

Integral rounding, clock acquisition, origin selection, delivery, scheduling,
runtime access, native execution, rendering, or physical execution are not
included.
