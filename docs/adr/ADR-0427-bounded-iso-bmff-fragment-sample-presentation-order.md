# ADR-0427: Bounded ISO BMFF Fragment Sample Presentation Order

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Order one rational-time fragment projection by exact presentation numerator,
then exact decode numerator, then original sample index. Require the source
index to match its immutable input position and every sample denominator to
match the projection's nonzero media timescale. Preserve the original projection
and return a separate immutable order.

## Non-goals

Cross-track or cross-fragment merge, clock conversion, scheduling, `mdat`
admission, payload views or ownership, decoder packet composition, traversal,
streaming, native execution, rendering, or physical execution are not included.
