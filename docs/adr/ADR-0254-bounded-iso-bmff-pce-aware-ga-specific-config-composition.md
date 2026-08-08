# ADR-0254: Bounded ISO BMFF PCE-Aware GA Specific Config Composition

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0250 projects the common GA prefix and ADR-0252 projects a complete
syntactic PCE. The remaining object-type-specific GA fields must resume after
the PCE when channel configuration is zero, or immediately after the prefix
otherwise.

## Decision

Add a pure managed composer that:

- requires or rejects a PCE according to the prefix channel configuration;
- requires exact prefix-to-PCE source identity;
- resumes at the PCE or prefix consumed bit position as appropriate;
- projects scalable layer, ER BSAC, resilience, and `extensionFlag3` fields with
  the same object-type conditions as ADR-0236; and
- returns the established GA Specific Config projection with exact provenance.

## Consequences

Channel configuration zero now reaches the same bounded GA result as indexed
channel configurations without duplicating or guessing PCE length.

## Non-goals

ER post-configuration sync extensions, speaker mapping, decoder extradata,
native calls, and physical execution are not included.
