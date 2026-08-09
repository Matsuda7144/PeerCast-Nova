# ADR-0325: Bounded ISO BMFF Movie Codec Packet Event Queue Dispatch Invocation Intent

## Status

Accepted as a post-0.0.10e non-physical managed-demux invocation-intent slice.

## Context

ADR-0324 binds capability resolutions to opaque runtime identities. The next
boundary needs to identify one caller-requested, source-ordered invocation batch
without obtaining or invoking runtime instances.

## Decision

Project every bounded runtime-target lookup into an immutable invocation intent.
Require one caller-explicit nonempty invocation ID for the whole projection and
assign each intent its exact zero-based source sequence index. Retain the exact
lookup and expose its request, runtime-instance ID, and nonzero generation.

Revalidate the existing 131,072-lookup bound, positional resolution identity,
runtime identity, and nonempty request payload. Fail closed for missing input,
an empty invocation ID, foreign or reordered lookup metadata, invalid runtime
identity, or an empty payload.

Invocation intent is identity and ordering metadata only. It does not acquire a
runtime instance, retain an invocable object, invoke a target, mutate a cursor,
or dispatch bytes.

## Consequences

One caller-requested batch can be correlated across exact source-ordered runtime
identities without granting runtime access or claiming delivery.

## Non-goals

Runtime instance access, leases, delegates, callbacks, invocation, actual
dispatch, cursor mutation, acknowledgement, delivery outcome, scheduling,
native packet submission, decoder execution, rendering, and physical execution
are not included.
