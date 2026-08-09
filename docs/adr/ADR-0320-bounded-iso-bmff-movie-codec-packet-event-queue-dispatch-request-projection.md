# ADR-0320: Bounded ISO BMFF Movie Codec Packet Event Queue Dispatch Request Projection

## Status

Accepted as a post-0.0.10e non-physical managed-demux request slice.

## Context

ADR-0319 binds exact candidates to opaque target identities but does not expose
one self-contained request view for a later target resolver or dispatcher.

## Decision

Project every bounded binding into immutable request metadata retaining the
exact binding while exposing its opaque target ID, media kind, exact codec
packet and payload, and deadline. Preserve source order and exact object
identity; do not copy or reinterpret compressed bytes.

Revalidate the existing 131,072-binding bound, positional candidate identity,
exact target-set membership, target media kind, and nonempty packet payload.
Fail closed for a missing source or any inconsistency.

The request is data only. It contains no delegate, callback, resolved target,
native handle, invocation, cursor mutation, or delivery result.

## Consequences

A later managed resolver can consume one exact request shape without rebuilding
the candidate, timing, packet, or destination relationships.

## Non-goals

Target resolution, target invocation, actual dispatch, cursor mutation,
delivery outcome, clock acquisition, scheduling, native packet submission,
decoder execution, rendering, and physical execution are not included.
