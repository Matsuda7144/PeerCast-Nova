# ADR-0358: Caller-Explicit ISO BMFF Completed Merge-Event Traversal Deadline Observation

## Status

Accepted as a post-0.0.10e non-physical managed metadata slice.

## Decision

Classify every exact ADR-0357 deadline as pending, ready, or expired at one
caller-supplied signed `BigInteger` tick. Retain exact deadlines and signed
distances without reading a clock or returning partial output.

## Non-goals

Repeated observation, eligibility, dispatch, delivery, native execution,
rendering, or physical execution are not included.
