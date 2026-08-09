# PCN-0483: Exact ISO BMFF Fragment Sample Merge Traversal Submission Manifest Completed Segment View

## Scope reviewed

- exact completed-view, source-segment, cursor-step, and segment references;
- resumed-index preservation and source/count mismatch rejection;
- explicit absence of acknowledgement and submission/delivery completion claims.

## Verification

- focused managed tests cover full and resumed reconstruction plus missing input;
- repository routine managed verification remains required for the cumulative
  worktree.

## Result

Accepted as the non-acknowledging completed view defined by ADR-0473.
