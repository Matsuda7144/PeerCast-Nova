# ADR-0463: Caller-Explicit ISO BMFF Fragment Sample Merge Traversal Submission Classification

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Attach one caller-explicit `Include` or `Exclude` classification to every exact
submission intent. Require an exact disposition count, retain each intent by
reference and in source order, reject undefined enum values, and expose exact
included and excluded counts. The classification is descriptive metadata and
does not grant authorization or perform selection.

## Non-goals

Authorization, policy inference, candidate selection, scheduling, runtime
lookup, capability access, payload access, invocation, dispatch, delivery,
native execution, rendering, or physical execution are not included.
