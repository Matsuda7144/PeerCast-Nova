# ADR-0281: Bounded ISO BMFF ER Error Protection Third-Set First Class

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Decision

Parse the third set's first class from the exact ADR-0280 post-count position,
retain the third-set header and class index zero, reuse the bounded conditional
class syntax internally, distinguish missing and empty headers, and return no
partial class on truncation.

## Non-goals

Remaining classes, reordered output, later sets, header protection, decoder or
native integration, and physical execution are not included.
