# PCN-0281: Bounded ISO BMFF AAC Sample-Entry Managed Decoder Preparation

## Scope

Review the composition of explicit sample-entry consistency admission with the
existing managed AAC decoder-preparation owner.

## Findings

- Missing sample-entry or format-intent sources fail before preparation.
- Strict mismatches fail before any disposable owner is returned.
- Effective decoder facts are admitted only under the explicit allow policy and
  retain the override marker.
- Exact source identities remain reachable through the consistency decision and
  decoder preparation.
- Success returns one owner that disposes the complete managed preparation.
- Partial preparation is disposed on failure.
- No runtime discovery, native operation, decoder open, or physical execution
  occurs.

## Decision

Accept sample-entry-aware managed AAC decoder preparation. Verified runtime
capability binding, native parameter transfer, decoder integration, and physical
execution remain incomplete.
