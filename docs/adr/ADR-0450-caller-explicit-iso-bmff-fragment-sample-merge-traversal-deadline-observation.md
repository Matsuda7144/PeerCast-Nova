# ADR-0450: Caller-Explicit ISO BMFF Fragment Sample Merge Traversal Deadline Observation

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Classify every decode and presentation deadline independently as pending, ready,
or expired at one caller-explicit signed arbitrary-precision observation tick.
Use exact half-open boundaries, retain ticks until each ready and expiration
boundary, and apply the same caller value to the entire bounded snapshot without
reading a clock.

## Non-goals

Clock acquisition, repeated observation, eligibility policy, candidate
selection, scheduling, delivery attempt or outcome, payload access, decoder
submission, native execution, rendering, or physical execution are not
included.
