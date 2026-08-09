# PCN-0351: Bounded ISO BMFF Cross-Track Merge-Step Sequence

## Decision

Accept immutable ownership of at most 262,144 exact merge steps with contiguous
indexes, one caller-explicit tie-break, and reference-exact cursor-state chaining.
No automatic traversal, completeness claim, consumption, delivery, native
operation, rendering, or physical execution occurs.
