# ADR-0239: Bounded ISO BMFF Explicit-extension GA Composition

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0238 identifies the core Audio Object Type and exact ending bit position of
an explicit SBR/PS extension. The GASpecificConfig parser from ADR-0236 must be
able to consume that core configuration without treating the leading extension
type as the core codec.

## Decision

Extend the pure managed GASpecificConfig boundary so that:

- its original direct AudioSpecificConfig path remains unchanged;
- a distinct explicit-extension entry point requires an ADR-0238 result;
- core object type and start bit position come only from that owned result;
- channel configuration continues to come from the common AudioSpecificConfig
  header; and
- the output identifies both the effective core object type and optional source
  extension.

## Consequences

Explicit HE-AAC/HE-AACv2 metadata can reach the same bounded General Audio field
projection used by direct AAC configurations while preserving its signalling
chain.

## Non-goals

No implicit sync-extension search, Program Config Element, channel-layout
resolution, decoder submission, native call, demux event, or physical execution
is included.
