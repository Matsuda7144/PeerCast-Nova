# ADR-0277: Bounded ISO BMFF ER Error Protection Second-Set Header

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0276 exposes the exact end of the first predefined set. When the fixed prefix
declares at least two sets, the second set's 6-bit class count begins at that
position rather than beside the first count.

## Decision

Add a pure managed parser that:

- requires and retains the exact ADR-0276 first set;
- distinguishes a missing source from a prefix declaring only one set;
- reads the second set's 6-bit class count only when at least two sets were
  declared;
- records set index one and the exact consumed bit position; and
- returns no partial header on truncation.

## Consequences

Second-set class traversal can begin from the actual nested syntax position
without scanning or inferring a contiguous class-count vector.

## Non-goals

Second-set class bodies, third and later sets, aggregate limits, header protection,
complete config traversal, `directMapping`, decoder configuration, native calls,
and physical execution are not included.
