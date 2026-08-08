# ADR-0255: Bounded ISO BMFF ER Audio Post Config

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ER General Audio object types carry a two-bit `epConfig` after their completed
GA Specific Config. Values two and three introduce an additional Error
Protection Specific Config whose variable structure is not yet implemented.

## Decision

Add a pure managed parser that:

- applies only to ER object types 17, 19, 20, 21, 22, and 23;
- starts at the exact ADR-0254 consumed position;
- projects supported `epConfig` values zero and one with source provenance;
- reports non-ER configurations as not requiring this field; and
- rejects values two and three as requiring an unsupported Error Protection
  Specific Config rather than guessing its length.

## Consequences

Supported ER configurations expose the exact position after `epConfig`, ready
for later sync-extension handling.

## Non-goals

Error Protection Specific Config, direct mapping, sync extensions, decoder
configuration, native calls, and physical execution are not included.
