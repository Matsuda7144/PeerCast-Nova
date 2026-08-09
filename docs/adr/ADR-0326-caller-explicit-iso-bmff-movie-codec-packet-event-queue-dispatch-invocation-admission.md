# ADR-0326: Caller-Explicit ISO BMFF Movie Codec Packet Event Queue Dispatch Invocation Admission

## Status

Accepted as a post-0.0.10e non-physical managed-demux admission-metadata slice.

## Context

ADR-0325 identifies one immutable invocation-intent batch without runtime
access. A caller needs to classify each exact intent as admitted or denied
before any later runtime-access design is considered.

## Decision

Project one caller-explicit `Admitted` or `Denied` disposition for every
source-ordered invocation intent. Retain the exact intent object and expose a
derived `IsAdmitted` value without retaining the caller's mutable disposition
list.

Revalidate the existing 131,072-intent bound, disposition count, positional
lookup identity, zero-based sequence index, shared nonempty invocation ID, and
opaque runtime-instance ID and nonzero generation. Fail closed for missing
input, count mismatch, invalid dispositions, foreign or reordered source
metadata, or invalid invocation/runtime identity.

Admission is immutable classification metadata only. It is not an execution
authorization packet and does not acquire a runtime instance, create a lease,
retain an invocable object, invoke a target, mutate a cursor, or dispatch bytes.

## Consequences

Later runtime-access design can consume an exact caller decision without
inferring permission or losing source identity.

## Non-goals

Runtime instance access, lease ownership, delegates, callbacks, invocation,
actual dispatch, cursor mutation, acknowledgement, delivery outcome,
scheduling, native packet submission, decoder execution, rendering, and
physical execution are not included.
