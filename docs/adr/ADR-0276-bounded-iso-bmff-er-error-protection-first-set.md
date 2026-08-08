# ADR-0276: Bounded ISO BMFF ER Error Protection First Set

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0275 establishes one complete class boundary. The first predefined set can
therefore be traversed using its bounded 6-bit count, followed by the set-level
reordered-output declaration.

## Decision

Add a pure managed parser that:

- requires and retains the exact ADR-0274 first-set header;
- reuses the ADR-0275 conditional class parser for every declared class while
  retaining zero-based class indices;
- supports an empty set without allocating class objects;
- reads the set-level reordered-output flag after the final class;
- when reordered, reads one 6-bit output index per class and requires an exact
  permutation of the set's class indices; and
- returns no partial set on class truncation, order truncation, duplication, or
  out-of-range order values.

## Consequences

The exact end of the first set is available for bounded later-set traversal, and
reordering cannot reference an absent or duplicate class.

## Non-goals

Later predefined sets, aggregate class limits, FEC semantic validation, header
protection, complete config traversal, `directMapping`, decoder configuration,
native calls, and physical execution are not included.
