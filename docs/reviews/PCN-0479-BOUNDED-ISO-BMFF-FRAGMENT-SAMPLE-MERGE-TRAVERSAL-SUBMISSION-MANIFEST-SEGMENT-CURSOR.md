# PCN-0479: Bounded ISO BMFF Fragment Sample Merge Traversal Submission Manifest Segment Cursor

## Scope reviewed

- immutable exact source and bounded next-segment position;
- current/terminal and remaining-count facts without mutation or completion;
- no payload, runtime, invocation, dispatch, delivery, or physical path.

## Verification

- focused managed tests cover current, terminal, missing, and out-of-range
  positions;
- repository routine managed verification remains required for the cumulative
  worktree.

## Result

Accepted as the bounded segment-cursor boundary defined by ADR-0469.
