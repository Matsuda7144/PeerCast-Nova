# ADR-0409: Explicit ISO BMFF Projected Completed-Event Traversal Dispatch Target Capability Set

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Attach at most two caller-declared unique target/media capability records to
one exact ADR-0408 request projection. Retain the complete projected source,
opaque target IDs, media kinds, and positive maximum payload byte counts.

## Non-goals

Capability discovery, runtime target access, request submission, delivery,
invocation, native execution, rendering, or physical execution are not
included.
