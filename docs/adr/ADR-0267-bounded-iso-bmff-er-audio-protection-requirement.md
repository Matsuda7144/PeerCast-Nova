# ADR-0267: Bounded ISO BMFF ER Audio Protection Requirement

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0255 accepts `epConfig` zero and one but intentionally rejects two and three
before the unimplemented variable Error Protection Specific Config. A separate
fact projection can retain all four values and state whether that additional
structure is required without guessing or skipping its length.

## Decision

Add a pure managed projector that:

- applies only to ER object types 17, 19, 20, 21, 22, and 23;
- starts at the exact completed GA Specific Config bit position;
- projects every two-bit `epConfig` value with exact source provenance;
- marks values two and three as requiring Error Protection Specific Config;
- exposes the position immediately after `epConfig`; and
- keeps missing, non-ER, and truncated input distinct.

The existing ADR-0255 parser continues to reject values two and three because
this projection does not determine the additional structure's end position.

## Consequences

Callers can distinguish supported direct continuation from configurations that
need a future bounded protection parser, without accepting partial semantics.

## Non-goals

Error Protection Specific Config parsing or length inference, sync-extension
continuation after it, decoder configuration, native calls, and physical
execution are not included.
