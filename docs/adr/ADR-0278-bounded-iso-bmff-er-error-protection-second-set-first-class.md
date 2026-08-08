# ADR-0278: Bounded ISO BMFF ER Error Protection Second-Set First Class

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0277 retains the exact end of the second set's class-count field. A class
body must begin there and use the same conditional Error Protection class syntax
as the first set, while remaining owned by the second-set header.

## Decision

Add a pure managed parser that:

- requires and retains the exact ADR-0277 second-set header;
- distinguishes a missing header from an explicitly empty second set;
- starts the first class at the exact post-count bit position;
- reuses the bounded conditional class-field reader without exposing a
  first-set owner for the result;
- records class index zero and the exact consumed bit position; and
- returns no partial class on truncation.

## Consequences

Second-set class traversal now has a correctly owned first-class boundary that
can be extended to the remaining declared classes without rescanning the bit
stream.

## Non-goals

Remaining second-set classes, reordered output, third and later sets, aggregate
limits, header protection, complete config traversal, `directMapping`, decoder
configuration, native calls, and physical execution are not included.
