# ADR-0256: Bounded ISO BMFF ER Implicit SBR/PS Sync Extension

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0255 exposes the exact position after a supported ER `epConfig`. The same
optional SBR sync-extension syntax already bounded by ADR-0240 can begin there,
with ADR-0241 optionally following it for PS.

## Decision

Extend the managed implicit-SBR parser with an ER post-config entry point that:

- requires the exact ADR-0255 projection and begins at its consumed position;
- reuses the existing marker, extension object type, presence, and sampling
  frequency rules without weakening the direct non-ER entry point;
- retains the underlying GA source identity; and
- lets the existing optional PS parser continue from the projected SBR end.

## Consequences

Supported ER configurations can expose immediate implicit SBR and PS signalling
without guessing or rescanning the `epConfig` boundary.

## Non-goals

Error Protection Specific Config, non-immediate marker search, decoder
configuration, native calls, and physical execution are not included.
