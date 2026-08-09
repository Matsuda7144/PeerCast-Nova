# ADR-0321: Explicit ISO BMFF Movie Codec Packet Event Queue Dispatch Target Capability Set

## Status

Accepted as a post-0.0.10e non-physical managed-demux capability slice.

## Context

ADR-0320 exposes request metadata but target identities alone do not declare
whether a request's media kind and payload size are supported. Capability must
remain separate from an invocable target.

## Decision

Own one or two caller declarations containing a nonempty target `Guid`, video
or audio media kind, and positive maximum payload byte count. Reject excessive,
duplicate-ID, duplicate-media-kind, invalid-ID, unsupported-kind, and invalid
limit declarations. Snapshot declarations as immutable capability objects.

The capability set contains no delegate, callback, native handle, target
instance, or invocation mechanism.

## Consequences

Target support and payload capacity are bounded, explicit, and reviewable
before a request is resolved.

## Non-goals

Request resolution, target lookup, target invocation, actual dispatch, cursor
mutation, delivery outcome, scheduling, native packet submission, rendering,
and physical execution are not included.
