# ADR-0219: Exact Rational ISO BMFF Sample/edit Movie Overlap

## Status

Accepted as a post-0.0.10e non-physical projection slice.

## Context

ADR-0218 retains exact overlap boundaries in media units. Converting those
boundaries back to movie units may produce fractions even when the complete
edit duration itself converts exactly.

## Decision

Add a pure managed projector that:

- accepts a bounded media-overlap set and the timing inputs that produced it;
- traverses referenced edit entries once in increasing order;
- validates that each overlap still belongs to the supplied edit interval;
- projects absolute movie bounds as signed `Int128` numerators over the media
  timescale denominator; and
- exposes whether each bound is an integral movie unit without rounding it.

Repeated edit appearances remain separate. Mismatched edit lists, timing, rate,
or overlap metadata fail closed.

## Consequences

Callers retain exact movie-timeline overlap positions even when boundaries fall
between integer movie units.

## Non-goals

No rounding policy, `TimeSpan` conversion, partial-sample admission, packet
selection, payload read, decoder composition, recording, or physical execution
is included.
