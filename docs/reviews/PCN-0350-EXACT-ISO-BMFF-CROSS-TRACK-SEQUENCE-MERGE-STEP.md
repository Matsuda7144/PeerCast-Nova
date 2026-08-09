# PCN-0350: Exact ISO BMFF Cross-Track Sequence Merge Step

## Decision

Accept one caller-indexed immutable cross-track merge step retaining the exact
selected stream, entry, packet, leading appearance, transition, and successor
cursor set. No repeated traversal, merged-list ownership, consumption,
delivery, native operation, rendering, or physical execution occurs.
