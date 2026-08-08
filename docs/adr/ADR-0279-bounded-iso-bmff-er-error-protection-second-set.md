# ADR-0279: Bounded ISO BMFF ER Error Protection Second Set

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0278 establishes the correctly owned first class of the second predefined
Error Protection set. The remaining declared classes and the set-local output
ordering fields must be consumed before a later set can be located.

## Decision

Add a pure managed parser that:

- requires and retains the exact ADR-0277 second-set header;
- traverses exactly the declared number of classes from the post-count position;
- records every class index and exact class end while retaining second-set
  ownership;
- consumes the set-local `reordered_output` flag after the final class;
- when reordering is enabled, accepts only an exact permutation of all declared
  class indexes; and
- returns no partial set for truncation or an invalid output order.

## Consequences

The second predefined set now has an exact owned end position suitable for
locating a third set without inferring a contiguous header vector.

## Non-goals

Third and later sets, general set aggregation, aggregate limits, header
protection, complete config traversal, `directMapping`, decoder configuration,
native calls, and physical execution are not included.
