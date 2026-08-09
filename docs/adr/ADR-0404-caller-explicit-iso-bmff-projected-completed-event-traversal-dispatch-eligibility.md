# ADR-0404: Caller-Explicit ISO BMFF Projected Completed-Event Traversal Dispatch Eligibility

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply one caller-explicit ready-only or ready-or-expired policy to every exact
ADR-0403 observation. Retain the complete projected source and policy, and
classify eligibility without dispatching or mutating an event.

## Non-goals

Candidate selection, target binding, delivery, scheduling, runtime access,
native execution, rendering, or physical execution are not included.
