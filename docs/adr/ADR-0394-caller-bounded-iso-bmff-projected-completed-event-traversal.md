# ADR-0394: Caller-Bounded ISO BMFF Projected Completed-Event Traversal

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply the existing completed-event traverser to the exact ADR-0393 initial
cursor under one positive caller event bound. Retain the full projected source,
the exact initial cursor, the ordered acknowledgement transitions, the final
cursor, and explicit exhaustion versus truncation.

## Non-goals

Continuation, completion certification, clock conversion, delivery,
scheduling, runtime access, native execution, rendering, or physical execution
are not included.
