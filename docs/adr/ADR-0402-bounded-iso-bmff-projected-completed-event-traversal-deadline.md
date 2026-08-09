# ADR-0402: Bounded ISO BMFF Projected Completed-Event Traversal Deadline

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply the existing bounded deadline projector to every exact ADR-0401 timeline
interval. Retain the complete projected source and expose immutable ready,
exclusive-expiration, and duration ticks with exact event ordering and source
identity.

## Non-goals

Clock acquisition, current-tick observation, eligibility, delivery,
scheduling, runtime access, native execution, rendering, or physical execution
are not included.
