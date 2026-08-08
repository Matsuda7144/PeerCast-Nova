# ADR-0240: Bounded ISO BMFF Implicit SBR Sync Extension

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

Direct General Audio configurations can append an optional `0x2B7` sync
extension that signals SBR without using leading Audio Object Type `5` or `29`.
ADR-0236 records the exact ending bit of GASpecificConfig, allowing this
immediate optional syntax to be tested without scanning arbitrary positions.

## Decision

Add a pure managed parser that:

- accepts only direct non-ER General Audio core types whose next syntax starts
  immediately after ADR-0236;
- rejects configurations already sourced through ADR-0238 explicit signalling;
- treats insufficient marker bits or a different marker as not present;
- requires marker `0x2B7` and extension Audio Object Type `5`;
- projects the SBR presence flag and, when present, indexed or explicit nonzero
  extension sampling frequency; and
- retains the source GASpecificConfig and exact consumed bit position.

## Consequences

Implicit SBR signalling for the supported immediate path is bounded and remains
distinguishable from absence, malformed syntax, and explicit signalling.

## Non-goals

No arbitrary-position scan, ER `epConfig` traversal, ER BSAC sync-extension
form, Program Config Element, decoder composition, native call, demux event, or
physical execution is included.
