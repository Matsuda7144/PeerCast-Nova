# PCN-0327: Bounded ISO BMFF Movie Codec Packet Event Queue Dispatch Candidate Selection

## Decision

Accept a caller-bounded immutable source-ordered prefix of ADR-0316 eligible
entries with total eligible count and truncation metadata. Empty selections are
valid. No target invocation, dispatch, cursor mutation, scheduling, native
operation, rendering, or physical execution occurs.
