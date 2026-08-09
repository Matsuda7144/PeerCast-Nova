# PCN-0470: Caller-Explicit ISO BMFF Fragment Sample Merge Traversal Consumer Batch Identity

## Scope reviewed

- factory-only construction of one immutable opaque batch identity;
- explicit rejection of missing or empty identifiers;
- no time, runtime, payload, dispatch, delivery, or physical semantics.

## Verification

- focused managed tests cover exact identity retention and invalid inputs;
- repository routine managed verification remains required for the cumulative
  worktree.

## Result

Accepted as the batch-identity boundary defined by ADR-0460.
