# PCN-0346: Exact ISO BMFF Selected Sequence Packet Head Snapshot

## Decision

Accept a non-destructive snapshot of the exact current entries for selected
video and/or audio sequence cursors, with explicit role and whole-set exhaustion.
Source, position, and entry-index mismatches fail closed without a partial
snapshot. No comparison, cursor advance, merge, native operation, rendering, or
physical execution occurs.
