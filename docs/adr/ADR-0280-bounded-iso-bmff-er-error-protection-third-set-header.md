# ADR-0280: Bounded ISO BMFF ER Error Protection Third-Set Header

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0279 exposes the exact end of the second predefined set. When the fixed
prefix declares at least three sets, the third set's class count begins there.

## Decision

Add a pure managed parser that requires and retains the exact second set,
distinguishes a missing source from a prefix declaring fewer than three sets,
reads the third set's 6-bit class count at the second-set end, records set index
two and the exact consumed position, and returns no partial header on truncation.

## Consequences

Third-set class traversal can begin at the actual nested syntax position.

## Non-goals

Third-set class bodies, fourth and later sets, general aggregation, header
protection, complete config traversal, decoder configuration, native calls, and
physical execution are not included.
