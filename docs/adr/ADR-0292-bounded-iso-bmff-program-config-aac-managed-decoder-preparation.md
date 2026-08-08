# ADR-0292: Bounded ISO BMFF Program Config AAC Managed Decoder Preparation

## Status

Accepted as a post-0.0.10e non-physical decoder-composition prerequisite.

## Context

ADR-0291 owns a validated PCE source-to-platform reorder plan. ADR-0268 owns the
managed AAC decoder-preparation chain. Combining unrelated PCE and decoder facts
would make the retained layout metadata unsafe to apply later.

## Decision

Add a managed-only composer that requires the reorder plan's exact PCE topology
to be the same topology retained by the decoder-format intent. Only after this
identity check may the existing bounded AAC preparation composer run. Retain the
exact plan and resulting disposable preparation owner.

Missing inputs, topology mismatch, or downstream preparation failure return no
partial preparation.

## Consequences

PCE layout intent and managed decoder parameters now remain source-bound in one
owner before any native transfer or sample-buffer operation.

## Non-goals

Reorder application, sample access, native channel-layout writes, decoder
creation or opening, device activation, and physical execution are not included.
