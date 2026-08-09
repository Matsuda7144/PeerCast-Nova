# ADR-0359: Caller-Explicit ISO BMFF Completed Merge-Event Traversal Dispatch Eligibility

## Status

Accepted as a post-0.0.10e non-physical managed metadata slice.

## Decision

Classify ADR-0358 observations under caller-explicit `ReadyOnly` or
`ReadyOrExpired` policy. Pending is never eligible, ready is always eligible,
and expired eligibility is explicit. Retain exact observations and source order,
failing atomically for invalid policy or source mismatch.

## Non-goals

Candidate selection, dispatch, delivery, runtime access, native execution,
rendering, or physical execution are not included.
