# ADR-0252: Bounded ISO BMFF Program Config Element

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0251 projects the fixed PCE header and bounded element counts. The remaining
conditional mixdown fields, count-driven element records, alignment, and comment
must be traversed exactly before later GA fields can be located.

## Decision

Add a pure managed parser that:

- requires the exact ADR-0251 header and continues at its consumed bit position;
- conditionally projects mono, stereo, and matrix mixdown fields;
- reads front, side, back, LFE, associated-data, and valid-CC records using only
  the fixed-width counts already projected by that header;
- requires zero byte-alignment bits;
- reads the 8-bit-sized comment into privately owned storage; and
- retains exact source provenance and final consumed bit position.

## Consequences

The complete syntactic PCE is available through a finite managed boundary, and
callers can locate the following GA fields without guessing variable lengths.

## Non-goals

Channel-layout derivation, semantic validation across element tags, later GA
fields, decoder configuration, native calls, and physical execution are not
included.
