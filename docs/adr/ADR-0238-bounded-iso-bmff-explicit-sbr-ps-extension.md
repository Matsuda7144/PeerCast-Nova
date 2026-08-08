# ADR-0238: Bounded ISO BMFF Explicit SBR/PS Extension

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

An AudioSpecificConfig whose leading Audio Object Type is `5` or `29` carries
explicit SBR or PS signalling before the core audio configuration. ADR-0235
retains the exact owned bytes and the bit position needed to parse that
extension without scanning unrelated data.

## Decision

Add a pure managed parser that:

- accepts only leading Audio Object Type `5` or `29`;
- starts at the exact bit position projected by ADR-0235;
- parses indexed or explicit nonzero extension sampling frequency;
- parses normal or extended core Audio Object Type;
- projects the additional channel configuration defined for core ER BSAC; and
- retains explicit SBR/PS presence and the exact consumed bit position.

## Consequences

Explicit HE-AAC and HE-AACv2 signalling is bounded and available for later core
configuration selection without guessing frequency or profile semantics.

## Non-goals

No implicit sync-extension search, GASpecificConfig composition, Program Config
Element, decoder extradata composition, native call, demux event, or physical
execution is included.
