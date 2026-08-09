# PCN-0478: Bounded ISO BMFF Fragment Sample Merge Traversal Submission Manifest Segments

## Scope reviewed

- bounded contiguous partitioning by exact fragment batch and sequence identity;
- exact manifest entry references, half-open ranges, order, and byte totals;
- no packet, payload, runtime, dispatch, delivery, or physical path.

## Verification

- focused managed tests cover multiple adjacent runs, empty projection, and
  missing input;
- repository routine managed verification remains required for the cumulative
  worktree.

## Result

Accepted as the non-executing segment projection defined by ADR-0468.
