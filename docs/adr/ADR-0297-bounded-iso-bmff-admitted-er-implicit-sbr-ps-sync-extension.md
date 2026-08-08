# ADR-0297: Bounded ISO BMFF Admitted ER Implicit SBR/PS Sync Extension

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0256 enters implicit SBR/PS parsing after `epConfig` zero or one. ADR-0288
now owns the exact complete configuration end for all values zero through three,
including Error Protection Specific Config and admitted direct mapping.

## Decision

Add a source-bound implicit-SBR parser entry point that requires one exact
ADR-0288 admission and starts at its consumed bit length. Reuse the established
marker, object-type, presence, and sampling-frequency rules, retain the exact GA
source, and allow the existing PS parser to continue from the resulting SBR end.

Keep the ADR-0256 entry point unchanged for compatibility. Missing admission
returns a distinct result and no extension.

## Consequences

Immediate implicit SBR and PS signalling can now follow admitted `epConfig` two
or three without guessing or rescanning variable protection syntax.

## Non-goals

Non-immediate marker search, decoder integration, runtime loading, native calls,
rendering, and physical execution are not included.
