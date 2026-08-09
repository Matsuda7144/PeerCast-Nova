# ADR-0444: Caller-Explicit ISO BMFF Fragment Sample Merge Traversal Integral Time Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Convert exact rational fragment acknowledgement timing to a caller-explicit
nonzero tick rate using independently caller-selected start and end rounding:
floor, ceiling, or require-exact. Project both decode and presentation
half-open intervals with arbitrary-precision arithmetic, reject non-integral
require-exact values and empty rounded intervals, and implement mathematically
correct floor and ceiling for negative presentation timestamps.

## Non-goals

Default tick rate or rounding policy, clock acquisition, clock origin, deadline
creation, scheduling, delivery attempt or outcome, payload access, decoder
submission, native execution, rendering, or physical execution are not
included.
