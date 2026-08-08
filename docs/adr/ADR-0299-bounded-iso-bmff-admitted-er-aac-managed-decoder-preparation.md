# ADR-0299: Bounded ISO BMFF Admitted ER AAC Managed Decoder Preparation

## Status

Accepted as a post-0.0.10e non-physical managed-decoder preparation slice.

## Context

ADR-0298 retains one exact ER configuration admission together with the AAC
decoder-format intent resolved from its optional immediate SBR/PS continuation.
ADR-0289 can prepare managed AAC decoder parameters from a separately supplied
admission and format intent, but it does not retain the ADR-0298 composition as
one source object.

## Decision

Accept the exact ADR-0298 composition and pass its admission and format intent
to the existing ER AAC managed decoder preparation composer. On success, own
the exact composition and the exact resulting preparation behind one disposable
result. Reject a missing composition or any downstream preparation failure
without returning a partial owner.

The preparation remains managed-only. Declared symbol names and decoder
capability limits remain caller assertions rather than verified runtime facts.

## Consequences

The admitted ER configuration, immediate SBR/PS facts, effective AAC format,
owned padded codec parameters, transfer plan, declared-symbol coverage, and
declared decoder-capability binding now form one source-bound managed ownership
chain with deterministic synchronous and asynchronous cleanup.

This closes the current non-physical managed-only ISO BMFF AAC metadata-to-
decoder-preparation chain. The separately established PCE explicit-layout path
remains available when a Program Config Element supplies channel topology.

## Non-goals

Runtime loading, ABI validation, address resolution, native parameter transfer,
decoder-context mutation or open, non-immediate sync-extension search,
rendering, and physical execution are not included.
