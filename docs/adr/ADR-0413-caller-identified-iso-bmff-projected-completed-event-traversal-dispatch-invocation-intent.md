# ADR-0413: Caller-Identified ISO BMFF Projected Completed-Event Traversal Dispatch Invocation Intent

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply one non-empty caller invocation ID to every exact ADR-0412 identity-only
lookup in source order. Retain the complete projected source, zero-based
sequence index, exact request, opaque runtime-instance ID, and generation as
immutable intent metadata.

## Non-goals

Authorization, admission, runtime object access, target calls, request
submission, delivery, invocation, native execution, rendering, or physical
execution are not included.
