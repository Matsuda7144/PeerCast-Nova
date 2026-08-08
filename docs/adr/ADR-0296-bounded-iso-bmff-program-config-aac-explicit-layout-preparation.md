# ADR-0296: Bounded ISO BMFF Program Config AAC Explicit Layout Preparation

## Status

Accepted as a post-0.0.10e non-physical decoder-composition prerequisite.

## Context

ADR-0292 prepares source-bound PCE AAC decoder parameters, ADR-0294 projects the
explicit mask requirement, and ADR-0295 verifies its declared symbol. Callers
should not reproduce this sequence or its partial-failure cleanup.

## Decision

Add a managed-only composer that performs the three established stages in order
and returns one disposable owner only after all succeed. Publish the exact union
of the existing AAC transfer symbols and `av_channel_layout_from_mask` as the
required declared-symbol set. Retain the decoder preparation, layout requirement,
and layout symbol coverage in one source chain.

Dispose the decoder preparation on every failure after its creation and delegate
successful synchronous or asynchronous disposal to that owner.

## Consequences

The complete managed PCE explicit-layout preparation has one entry point, one
required symbol set, deterministic cleanup, and no partial success state.

## Non-goals

Runtime loading, address lookup, ABI checks, native parameter transfer, decoder
creation or opening, rendering, and physical execution are not included.
