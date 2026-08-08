# ADR-0289: Bounded ISO BMFF ER AAC Managed Decoder Preparation

## Status

Accepted as a post-0.0.10e non-physical decoder-composition prerequisite.

## Context

ADR-0288 admits a complete source-bound ER configuration, while ADR-0268
builds the disposable managed AAC decoder-preparation chain. The two boundaries
must not be combined when they originate from different GA configurations.

## Decision

Add a managed-only composer that requires an admitted ER configuration and an
AAC decoder-format intent derived from the exact same GA Specific Config. After
that identity check, delegate to the existing bounded decoder-preparation
composer and retain both the admission and the resulting disposable owner.

Missing inputs, source mismatch, or downstream preparation failure produce no
partial ER preparation. Disposal delegates to the existing managed preparation
owner.

## Consequences

Admitted `epConfig` state now remains attached to the exact managed AAC
parameter-preparation chain before any native parameter transfer.

## Non-goals

Runtime capability discovery, native parameter transfer, decoder creation or
opening, packet submission, and physical execution are not included.
