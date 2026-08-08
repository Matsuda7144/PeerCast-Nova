# ADR-0263: Bounded ISO BMFF AAC Parameter Transfer Plan

## Status

Accepted as a post-0.0.10e non-physical decoder-composition prerequisite.

## Context

ADR-0262 owns validated, padded AAC codec parameters. Before any native decoder
context is selected or changed, the exact transfer requirements and ownership
must be representable as a managed-only plan.

## Decision

Add a bounded transfer planner that:

- requires an available ADR-0262 managed parameter owner;
- verifies AAC codec identity and exact sample-rate, channel-count, and
  extradata-length agreement with the retained ADR-0261 input;
- retains that owner without copying or exposing extradata bytes;
- projects the five symbols required by the established FFmpeg parameter
  transfer boundary; and
- forwards synchronous and asynchronous disposal to the managed owner.

The plan records requirements only. It does not select a runtime, resolve a
symbol address, allocate native parameters, invoke a native function, select or
mutate a decoder context, transfer parameters, or open a decoder.

## Consequences

Later decoder binding can consume one source-bound owner and one explicit,
reviewable symbol requirement set without reconstructing ISO BMFF AAC facts.

## Non-goals

Native capability discovery, native parameter transfer, decoder-context
selection or mutation, decoder open, external runtime loading, and physical
execution are not included.
