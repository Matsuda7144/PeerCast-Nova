# PCN-0344: Explicit ISO BMFF Movie Track Codec Packet Sequence Selection

## Decision

Accept caller-explicit video and/or audio sequence selection from the exact
ADR-0333 set. Bounds, distinct-role, and media-kind mismatches fail closed. No
default inference, cursor creation, cross-track merge, native operation,
rendering, or physical execution occurs.
