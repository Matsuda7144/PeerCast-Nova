# ADR-0437: Exact ISO BMFF Fragment Sample Merge Cursor Step

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Project one immutable step from a non-complete merge cursor. Bind the exact
source cursor, source sample at its next index, and an immutable successor over
the same merge at index plus one. Report completion without manufacturing a
step and reject missing or structurally invalid cursors.

## Non-goals

Acknowledgement, delivery outcome, cursor mutation, clock acquisition,
scheduling, payload access, decoder submission, native execution, rendering,
or physical execution are not included.
