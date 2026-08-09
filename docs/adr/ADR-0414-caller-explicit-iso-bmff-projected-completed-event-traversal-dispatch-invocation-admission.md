# ADR-0414: Caller-Explicit ISO BMFF Projected Completed-Event Traversal Dispatch Invocation Admission

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply one caller-supplied admitted or denied disposition to every exact
ADR-0413 intent in source order. Retain the complete projected source and exact
intent identities, requiring disposition-count equality and valid opaque
invocation/runtime identities.

## Non-goals

Authorization acquisition, policy inference, target calls, request submission,
delivery, invocation, native execution, rendering, or physical execution are
not included.
