# ADR-0322: Bounded ISO BMFF Movie Codec Packet Event Queue Dispatch Target Resolution

## Status

Accepted as a post-0.0.10e non-physical managed-demux resolution slice.

## Context

ADR-0320 supplies bounded requests and ADR-0321 supplies declarative target
capabilities. They need an exact compatibility binding before any invocable
target is introduced.

## Decision

Resolve every bounded request to the exact capability with the same target ID.
Require identical media kind and a payload length no greater than the declared
maximum. Retain exact request, capability set, and capability objects in source
order.

Revalidate the existing 131,072-request bound and positional request identity.
Fail closed for missing inputs, source mismatch, absent capability, media
mismatch, or payload-limit excess.

Resolution produces metadata only. It does not locate a runtime object, invoke
a callback, mutate a cursor, or dispatch bytes.

## Consequences

Every request can be proven compatible with a declared target capability before
runtime target ownership is considered.

## Non-goals

Runtime target lookup, delegates, callbacks, target invocation, actual
dispatch, cursor mutation, delivery outcome, scheduling, native packet
submission, decoder execution, rendering, and physical execution are not
included.
