# ADR-0400: Caller-Explicit ISO BMFF Projected Completed-Event Traversal Integral Clock Time

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply caller-explicit floor, ceiling, or require-exact policies independently
to every ADR-0399 start and exclusive-end tick. Retain the complete projected
source and policies, reject non-integral require-exact values and empty rounded
intervals, and expose immutable integral tick intervals.

## Non-goals

Clock acquisition, origin selection, deadlines, delivery, scheduling, runtime
access, native execution, rendering, or physical execution are not included.
