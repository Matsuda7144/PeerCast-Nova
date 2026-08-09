# PCN-0318: Bounded ISO BMFF Movie Codec Packet Event Queue Snapshot

## Decision

Accept a bounded immutable queue snapshot that assigns exact ADR-0307 event
times deterministic zero-based sequence indexes while retaining their complete
source identity. Fail closed on limit, order, and exact-time inconsistencies.
No mutable queue, consumption, clock, scheduling, native operation, rendering,
or physical execution occurs.
