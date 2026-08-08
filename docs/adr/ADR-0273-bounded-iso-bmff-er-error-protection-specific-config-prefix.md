# ADR-0273: Bounded ISO BMFF ER Error Protection Specific Config Prefix

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0267 identifies `epConfig` values two and three as requiring Error Protection
Specific Config, but the complete structure contains nested per-set and per-class
fields. Only the fixed 16-bit prefix precedes that nested sequence and can be
projected without crossing a class body.

## Decision

Add a pure managed prefix parser that:

- requires the exact ADR-0267 requirement with `epConfig` two or three;
- begins at the exact post-`epConfig` bit position;
- projects the 8-bit predefined-set count, 2-bit interleave type, 3-bit bit
  stuffing value, and 3-bit concatenated-frame count;
- retains the exact requirement and resulting consumed bit position;
- applies a configurable predefined-set limit of 64 by default and 255 at most;
  and
- reports missing, not-required, invalid-limit, over-limit, and truncated input
  separately without returning a partial prefix.

## Consequences

The first set parser can start from a validated finite set count without rereading
`epConfig`. Later set counts remain behind their preceding set's class bodies.

## Non-goals

Set class counts, per-class attributes, escape fields, FEC semantics, complete config length,
`directMapping`, sync-extension continuation, decoder configuration, native
calls, and physical execution are not included.
