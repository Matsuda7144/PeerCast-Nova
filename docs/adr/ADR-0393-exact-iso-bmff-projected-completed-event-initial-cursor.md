# ADR-0393: Exact ISO BMFF Projected Completed-Event Initial Cursor

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply the existing immutable completed-event cursor factory to one exact
ADR-0392 event projection. Retain the exact event source and create an index-zero
cursor with the exact remaining event count, without advancing, reading,
acknowledging, consuming, or delivering an event.

## Non-goals

Cursor advance, traversal, acknowledgement, clock conversion, delivery,
scheduling, runtime access, native execution, rendering, or physical execution
are not included.
