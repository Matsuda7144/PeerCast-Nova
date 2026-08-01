# ADR-0218: Bounded ISO BMFF Sample/edit Overlap Lookup

## Status

Accepted as a post-0.0.10e non-physical lookup slice.

## Context

ADR-0217 projects supported edits into exact media intervals. One sample may
overlap no edit, one edit, or multiple repeated edits.

## Decision

Add a pure managed lookup that:

- accepts one exact sample presentation interval and parsed edit timing;
- validates the sample's half-open interval ordering;
- skips empty edits and compares all other projected half-open intervals;
- preserves every exact media intersection and whether it contains the whole
  sample;
- defaults to 4,096 overlaps with a 65,536 hard limit; and
- fails the whole lookup on unsupported rates or non-integral edit durations.

The edit parser's entry limit bounds a single forward traversal. Running movie
bounds are retained between entries, and no per-time-unit expansion or repeated
prefix scan is performed.

## Consequences

Callers can distinguish samples absent from the edited presentation from full
or partial appearances, including repeated edits.

## Non-goals

No movie-time overlap projection, partial-sample admission policy, packet
selection, payload read, decoder composition, recording, or physical execution
is included.
