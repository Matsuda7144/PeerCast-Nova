# ADR-0215: Exact Normal-rate ISO BMFF Edit-time Mapping

## Status

Accepted as a post-0.0.10e non-physical mapping slice.

## Context

ADR-0214 locates the edit segment containing one movie-timeline unit. Mapping
that relative position into media time requires both timescales and an explicit
media-rate policy.

## Decision

Add a pure managed mapper that:

- requires parsed edit-list, movie timing, and media timing inputs;
- composes the bounded edit-segment lookup;
- reports empty edits without inventing a media time;
- accepts only the exact normal 16.16 media rate `(1, 0)`;
- scales the segment-relative movie offset using `Int128`; and
- returns a signed exact media unit only when the timescale ratio is integral.

Missing inputs, range failures, empty edits, unsupported rates, and
non-integral results remain distinct fail-closed outcomes.

## Consequences

Normal-rate edit points that lie exactly on media-unit boundaries can be
mapped without floating point or premature `TimeSpan` rounding.

## Non-goals

No fractional-unit rounding, dwell or non-normal rate handling, interval
overlap, sample selection, payload read, decoder composition, recording, or
physical execution is included.
