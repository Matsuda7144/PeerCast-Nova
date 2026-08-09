# PCN-0314: Explicit ISO BMFF Movie Codec Stream Selection

## Decision

Accept caller-explicit video and/or audio stream selection from the exact
ADR-0303 index. Bounds, distinct-role, and media-kind mismatches fail closed.
No default inference, scheduling, native operation, rendering, or physical
execution occurs.
