# ADR-0356: Caller-Explicit ISO BMFF Completed Merge-Event Traversal Clock Origin

## Status

Accepted as a post-0.0.10e non-physical managed metadata slice.

## Decision

Add one caller-explicit signed `BigInteger` origin to every exact ADR-0355
interval while retaining source order and identity. Reject source mismatch or
invalid intervals atomically.

## Non-goals

Clock acquisition, deadline observation, delivery, scheduling, native execution,
rendering, or physical execution are not included.
