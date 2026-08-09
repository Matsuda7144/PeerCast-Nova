# ADR-0349: Caller-Bounded ISO BMFF Completed Merge-Event Traversal

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0347 advances one event cursor and ADR-0348 records one exact transition.
Callers need finite automatic traversal without converting cursor movement into
a delivery claim.

## Decision

Accept one exact ADR-0347 cursor and a positive caller event limit no greater
than 131,072. Repeatedly advance the immutable cursor and retain exact ADR-0348
acknowledgements until exhaustion or the caller limit. Return the exact initial
and final cursors, acknowledgement count, and distinct exhausted or truncated
state. Accept already-exhausted input as an empty exhausted traversal.

Fail closed for missing or invalid cursors, invalid bounds, failed advance, or
failed acknowledgement without returning partial output.

## Consequences

One bounded immutable event-cursor segment owns an exact transition chain with
explicit caller truncation and exhaustion.

## Non-goals

Continuation, whole-order completion proof, consumption, delivery, scheduling,
native packet submission, decoder execution, rendering, or physical execution
are not included.
