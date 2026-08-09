# PCN-0468: Caller-Explicit ISO BMFF Fragment Sample Merge Traversal Consumer Identity

## Scope reviewed

- factory-only construction of one immutable opaque consumer identity;
- explicit rejection of missing or empty identifiers;
- no runtime object, capability, payload, dispatch, delivery, or physical path.

## Verification

- focused managed tests cover exact identity retention and invalid inputs;
- repository routine managed verification remains required for the cumulative
  worktree.

## Result

Accepted as the bounded consumer-identity boundary defined by ADR-0458.
