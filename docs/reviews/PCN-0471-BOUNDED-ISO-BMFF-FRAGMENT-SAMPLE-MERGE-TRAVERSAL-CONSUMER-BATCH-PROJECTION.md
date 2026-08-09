# PCN-0471: Bounded ISO BMFF Fragment Sample Merge Traversal Consumer Batch Projection

## Scope reviewed

- bounded immutable batch entries over exact consumer bindings;
- contiguous batch-entry indexes and exact request/consumer source checks;
- no scheduler, runtime, payload, dispatch, delivery, or physical path.

## Verification

- focused managed tests cover populated and empty batches plus missing inputs;
- repository routine managed verification remains required for the cumulative
  worktree.

## Result

Accepted as the non-dispatch batch projection defined by ADR-0461.
