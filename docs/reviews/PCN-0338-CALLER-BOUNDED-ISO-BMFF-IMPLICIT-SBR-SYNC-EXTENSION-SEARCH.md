# PCN-0338: Caller-Bounded ISO BMFF Implicit SBR Sync Extension Search

## Decision

Accept one-bit-step `0x2B7` search after exact direct-GA or admitted-ER
configuration under a caller-explicit bounded candidate count, with distinct
not-found and limit-reached outcomes and exact marker provenance. Existing SBR
and following immediate PS parsing remain managed-only; no native operation,
decoder execution, rendering, or physical execution occurs.
