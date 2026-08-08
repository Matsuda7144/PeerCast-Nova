# ADR-0274: Bounded ISO BMFF ER Error Protection First-Set Header

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0273 stops at the exact fixed-prefix boundary. Error Protection Specific
Config then places each set's 6-bit class count immediately before that set's
class bodies, so later set counts cannot be projected without traversing the
preceding bodies.

## Decision

Add a pure managed parser that:

- requires the exact ADR-0273 prefix and retains its identity;
- rejects a prefix declaring no predefined sets;
- reads only the first set's 6-bit class count from the exact prefix end;
- records set index zero and the exact consumed bit position; and
- stops before the first class control field.

No later set count is read or inferred.

## Consequences

Per-class parsing can begin with an exact first-set class count without treating
nested class-body bits as a contiguous count vector.

## Non-goals

Class control fields, later set traversal, total-class projection, reordering,
header protection, complete config traversal, `directMapping`, decoder
configuration, native calls, and physical execution are not included.
