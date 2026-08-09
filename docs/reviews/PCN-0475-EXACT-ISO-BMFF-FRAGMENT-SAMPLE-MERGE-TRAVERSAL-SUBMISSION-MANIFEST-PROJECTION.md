# PCN-0475: Exact ISO BMFF Fragment Sample Merge Traversal Submission Manifest Projection

## Scope reviewed

- exact selected-intent flattening into immutable identity and range metadata;
- strictly increasing source order, exact inclusion, nonempty identity, and
  size-matched byte-range validation;
- no payload, authorization, runtime, invocation, dispatch, or physical path.

## Verification

- focused managed tests cover exact metadata, empty projection, missing input,
  and reordered-candidate rejection;
- repository routine managed verification remains required for the cumulative
  worktree.

## Result

Accepted as the non-executing exact manifest defined by ADR-0465.
