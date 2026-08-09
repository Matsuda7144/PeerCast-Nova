# PCN-0481: Bounded ISO BMFF Fragment Sample Merge Traversal Submission Manifest Segment Traversal

## Scope reviewed

- bounded exhaustive immutable cursor-step chain from initial to terminal cursor;
- resumed/terminal support and pre-allocation caller/hard step limits;
- no acknowledgement, payload, runtime, invocation, dispatch, or physical path.

## Verification

- focused managed tests cover exact exhaustion, resume, terminal, limits,
  invalid configuration, and invalid cursor state;
- repository routine managed verification remains required for the cumulative
  worktree.

## Result

Accepted as the bounded segment traversal defined by ADR-0471.
