# ADR-0323: Explicit ISO BMFF Movie Codec Packet Event Queue Dispatch Runtime Target Registry

## Status

Accepted as a post-0.0.10e non-physical managed-demux runtime-identity slice.

## Context

ADR-0322 proves declarative request compatibility but does not identify a
runtime target generation. Runtime identity must remain separate from any
object reference or invocation mechanism.

## Decision

Own one or two caller declarations containing the exact target ID, an opaque
nonempty runtime-instance `Guid`, video or audio media kind, and nonzero `ulong`
generation. Reject excessive, invalid, duplicate-target, duplicate-instance,
and duplicate-media declarations. Snapshot them as immutable registrations.

The registry contains identities and generations only. It owns no runtime
instance reference, delegate, callback, native handle, or invocation method.

## Consequences

Runtime target generations can be identified and compared without granting
access to executable target state.

## Non-goals

Request lookup, runtime instance access, invocation, actual dispatch, cursor
mutation, delivery outcome, scheduling, native packet submission, rendering,
and physical execution are not included.
