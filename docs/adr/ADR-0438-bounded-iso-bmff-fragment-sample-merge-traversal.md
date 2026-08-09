# ADR-0438: Bounded ISO BMFF Fragment Sample Merge Traversal

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Traverse all remaining entries from one immutable fragment sample merge cursor
through its exact one-step chain. Preflight a default remaining-step limit of
4,096 and hard limit of 65,536 before allocation. Preserve the exact initial
cursor, every source-bound step, and the final complete cursor, and validate
source identity plus contiguous indexes while constructing the traversal.

## Non-goals

Partial delivery batches, acknowledgement, delivery outcome, clock conversion,
scheduling, discontinuity action, `mdat` or payload access, decoder packet
composition, native execution, rendering, or physical execution are not
included.
