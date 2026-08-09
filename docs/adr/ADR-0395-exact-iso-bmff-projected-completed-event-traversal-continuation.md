# ADR-0395: Exact ISO BMFF Projected Completed-Event Traversal Continuation

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Continue one exact truncated ADR-0394 traversal from its final cursor under a
positive caller additional-event bound. Preserve the root projected cursor
source, require reference-exact segment continuity, and retain cumulative event
indexes without replaying prior acknowledgements.

## Non-goals

Completion certification, acknowledgement flattening, clock conversion,
delivery, scheduling, runtime access, native execution, rendering, or physical
execution are not included.
