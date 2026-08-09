# ADR-0403: Caller-Explicit ISO BMFF Projected Completed-Event Traversal Deadline Observation

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Classify every exact ADR-0402 deadline as pending, ready, or expired against one
caller-supplied signed observation tick. Retain the complete projected source,
the exact caller tick, deadline identities, and signed distances without
reading a clock.

## Non-goals

Clock acquisition, repeated observation, eligibility policy, delivery,
scheduling, runtime access, native execution, rendering, or physical execution
are not included.
