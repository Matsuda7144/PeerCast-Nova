# ADR-0396: Exact ISO BMFF Projected Completed-Event Traversal Completion

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply the existing completion factory to one exhausted ADR-0394/ADR-0395
projected traversal chain. Retain the root projected cursor source and certify
that the cumulative acknowledgement count equals the initial remaining-event
count and that the exact final cursor is exhausted.

## Non-goals

Acknowledgement flattening, clock conversion, delivery, scheduling, runtime
access, native execution, rendering, or physical execution are not included.
