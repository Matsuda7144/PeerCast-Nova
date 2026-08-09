# ADR-0388: Caller-Bounded ISO BMFF Projected-Sequence Merge Traversal

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply the existing cross-track merge traverser to the exact ADR-0387 initial
cursor set using one caller-explicit equal-interval tie policy and caller-bounded
step limit. Retain the exact cursor projection, inputs, traversal, final cursor
state, and explicit exhausted-versus-truncated result without partial output.

## Non-goals

Automatic tie policy, unbounded traversal, continuation, completion
certification, event projection, delivery, runtime access, native execution,
rendering, or physical execution are not included.
