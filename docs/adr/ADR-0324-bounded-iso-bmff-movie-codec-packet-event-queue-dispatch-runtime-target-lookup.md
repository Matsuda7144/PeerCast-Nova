# ADR-0324: Bounded ISO BMFF Movie Codec Packet Event Queue Dispatch Runtime Target Lookup

## Status

Accepted as a post-0.0.10e non-physical managed-demux runtime-lookup slice.

## Context

ADR-0322 retains capability-compatible requests and ADR-0323 retains opaque
runtime registrations. They need an exact identity binding before any runtime
instance access or invocation is considered.

## Decision

Look up every bounded resolution by exact target ID in the runtime registry.
Require the registration media kind to match both request and capability and
retain a nonzero generation. Preserve exact resolution, registry, and
registration objects in source order while exposing only opaque runtime ID and
generation.

Revalidate the existing 131,072-resolution bound and positional source
identity. Fail closed for missing inputs, foreign source metadata, absent
registration, media mismatch, or invalid generation.

Lookup produces identity metadata only. It does not return a runtime instance,
invoke a target, mutate a cursor, or dispatch bytes.

## Consequences

Compatible requests can be tied to exact runtime generations without granting
runtime-object access.

## Non-goals

Runtime instance access, delegates, callbacks, invocation, actual dispatch,
cursor mutation, delivery outcome, scheduling, native packet submission,
decoder execution, rendering, and physical execution are not included.
