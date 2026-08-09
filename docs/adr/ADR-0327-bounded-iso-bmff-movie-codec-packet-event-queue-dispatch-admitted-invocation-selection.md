# ADR-0327: Bounded ISO BMFF Movie Codec Packet Event Queue Dispatch Admitted Invocation Selection

## Status

Accepted as a post-0.0.10e non-physical managed-demux admission-selection
slice.

## Context

ADR-0326 retains caller-explicit admitted and denied dispositions for every
exact invocation intent. Downstream design needs a bounded admitted subset
without treating denied entries as executable work or accessing a runtime.

## Decision

Select admitted entries only, in exact source order, under a caller-explicit
positive maximum no greater than the existing 131,072-event bound. Retain the
exact admission objects, the caller maximum, the total admitted count before
truncation, and an explicit truncation indicator.

Revalidate the admission bound, positional intent identity, and disposition
values. Fail closed for missing input, invalid bounds, excessive admissions,
foreign or reordered source metadata, or invalid dispositions.

Selection is a pure immutable metadata operation. It does not acquire a runtime
instance, create a lease, retain an invocable object, invoke a target, mutate a
cursor, acknowledge delivery, or dispatch bytes.

## Consequences

The current managed chain ends with a bounded exact admitted subset whose
source order and truncation are explicit before any runtime-access design.

## Non-goals

Runtime instance access, lease ownership, delegates, callbacks, invocation,
actual dispatch, cursor mutation, acknowledgement, delivery outcome,
scheduling, native packet submission, decoder execution, rendering, and
physical execution are not included.
