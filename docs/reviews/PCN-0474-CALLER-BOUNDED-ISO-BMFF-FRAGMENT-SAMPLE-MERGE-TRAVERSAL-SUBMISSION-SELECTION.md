# PCN-0474: Caller-Bounded ISO BMFF Fragment Sample Merge Traversal Submission Selection

## Scope reviewed

- caller-bounded source-order prefix over exact `Include` classifications;
- exact source indexes, total included count, and truncation metadata;
- no authorization, payload, runtime, invocation, dispatch, or physical path.

## Verification

- focused managed tests cover truncation, exclusion skipping, source order, and
  invalid bounds;
- repository routine managed verification remains required for the cumulative
  worktree.

## Result

Accepted as the non-executing bounded selection defined by ADR-0464.
