# PCN-0286: Bounded ISO BMFF ER Error Protection First Set

## Scope

Review complete first-set class traversal and reordered-output parsing.

## Findings

- Every declared class is parsed in order from the prior class end.
- Class indices are contiguous and source-bound to the exact first-set header.
- Empty sets are supported without per-class allocation.
- Reordered output is either absent or an exact permutation of all class indices.
- Truncated, duplicate, and out-of-range order inputs produce no partial set.
- No later set, decoder, native operation, or physical execution occurs.

## Decision

Accept bounded first-set traversal. Later sets, header protection, complete
configuration continuation, and decoder integration remain incomplete.
