# ADR-0250: Bounded ISO BMFF GA Specific Config Prefix

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0236 rejects channel configuration zero before exposing the location at
which a Program Config Element begins. Both direct General Audio configurations
and the core configuration selected by ADR-0238 need the same exact prefix
projection before that optional structure can be parsed.

## Decision

Add a pure managed prefix parser that:

- accepts either a direct AudioSpecificConfig header or an explicit SBR/PS
  extension with exact source identity;
- permits only the General Audio object types already admitted by ADR-0236;
- projects `frameLengthFlag`, `dependsOnCoreCoder`, optional 14-bit
  `coreCoderDelay`, and `extensionFlag`;
- reports whether channel configuration zero requires a Program Config Element;
- retains the exact consumed bit position; and
- distinguishes missing source, unsupported object type, and truncation.

## Consequences

Callers can locate the optional Program Config Element without treating it as
unsupported or reparsing the leading AudioSpecificConfig fields.

## Non-goals

Program Config Element body parsing, later GA fields, decoder configuration,
native calls, and physical execution are not included.
