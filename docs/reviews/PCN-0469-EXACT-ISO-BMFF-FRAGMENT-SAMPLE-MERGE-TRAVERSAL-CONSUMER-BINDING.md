# PCN-0469: Exact ISO BMFF Fragment Sample Merge Traversal Consumer Binding

## Scope reviewed

- exact request-source and opaque-consumer reference binding;
- bounded, source-ordered, contiguous request validation;
- nonzero exact-track validation without runtime or payload access.

## Verification

- focused managed tests cover populated and empty bindings, missing inputs, and
  foreign request-source rejection;
- repository routine managed verification remains required for the cumulative
  worktree.

## Result

Accepted as the non-dispatch consumer-binding boundary defined by ADR-0459.
