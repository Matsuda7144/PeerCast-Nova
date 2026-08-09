# ADR-0319: Exact ISO BMFF Movie Codec Packet Event Queue Dispatch Target Binding

## Status

Accepted as a post-0.0.10e non-physical managed-demux binding slice.

## Context

ADR-0317 produces exact candidates and ADR-0318 owns opaque media target
identities. Candidates must be bound to a matching identity before any target
resolution or invocation is considered.

## Decision

Bind each bounded candidate to the exact ADR-0318 video or audio target matching
the candidate packet event's media kind. Retain the exact candidate, target
set, and target object in an immutable source-ordered projection.

Revalidate the existing 131,072-candidate bound, candidate source identity and
eligibility, required media target presence, and target media kind. Fail closed
for missing inputs, excessive count, foreign candidate metadata, missing media
target, or mismatched target kind.

Binding stores an opaque identity only. It does not resolve or invoke a target,
advance a cursor, or record delivery.

## Consequences

Every selected candidate can carry an exact destination identity without
introducing executable target ownership or side effects.

## Non-goals

Target resolution, delegates, callbacks, invocation, actual dispatch, cursor
mutation, delivery outcome, clock acquisition, scheduling, native packet
submission, decoder execution, rendering, and physical execution are not
included.
