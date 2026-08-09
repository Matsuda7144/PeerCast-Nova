# ADR-0318: Explicit ISO BMFF Movie Codec Packet Event Queue Dispatch Target Set

## Status

Accepted as a post-0.0.10e non-physical managed-demux ownership slice.

## Context

ADR-0317 owns candidate metadata but has no target identity boundary. Target
identity must be representable without storing delegates, callbacks, native
handles, or invocable objects.

## Decision

Own at most one video and one audio dispatch target as opaque caller-explicit
nonempty `Guid` identities. Require at least one target and reject duplicate
video/audio identities. Retain the media kind alongside each identity in an
immutable target set.

The target set identifies destinations only. It cannot resolve or invoke a
target and owns no executable or native resource.

## Consequences

Video and audio destination identities are explicit, bounded, immutable, and
independent from invocation mechanics.

## Non-goals

Candidate binding, target resolution, delegates, callbacks, actual dispatch,
cursor mutation, delivery outcome, scheduling, native packet submission,
decoder execution, rendering, and physical execution are not included.
