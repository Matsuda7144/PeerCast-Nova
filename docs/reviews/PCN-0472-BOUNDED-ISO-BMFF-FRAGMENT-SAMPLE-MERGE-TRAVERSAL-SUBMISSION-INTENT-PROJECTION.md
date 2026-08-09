# PCN-0472: Bounded ISO BMFF Fragment Sample Merge Traversal Submission Intent Projection

## Scope reviewed

- bounded immutable intent metadata over exact consumer-batch entries;
- contiguous source order and exact request, consumer, and batch identity;
- no payload, runtime, invocation, dispatch, delivery, or physical path.

## Verification

- focused managed tests cover populated and empty projections plus reordered
  entry rejection;
- repository routine managed verification remains required for the cumulative
  worktree.

## Result

Accepted as the non-invoking submission-intent boundary defined by ADR-0462.
