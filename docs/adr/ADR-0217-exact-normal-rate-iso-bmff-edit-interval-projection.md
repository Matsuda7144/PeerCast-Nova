# ADR-0217: Exact Normal-rate ISO BMFF Edit-interval Projection

## Status

Accepted as a post-0.0.10e non-physical projection slice.

## Context

ADR-0215 maps a single movie-timeline point through an edit. Sample admission
also needs the complete media interval exposed by one non-empty edit.

## Decision

Add a pure managed entry-indexed projector that:

- requires parsed edit-list, movie timing, and media timing inputs;
- computes exact half-open movie bounds without per-unit expansion;
- reports empty edits without assigning media bounds;
- accepts only the exact normal 16.16 media rate `(1, 0)`;
- converts segment duration with `Int128`; and
- returns exact signed half-open media bounds only when duration conversion is
  integral.

Missing inputs, invalid entry indices, empty edits, unsupported rates, and
non-integral durations are distinct fail-closed outcomes.

## Consequences

Callers can compare exact sample presentation intervals with one supported
edit interval without floating point or early `TimeSpan` rounding.

## Non-goals

No fractional rounding, dwell or non-normal rate handling, overlap policy,
sample admission, payload read, decoder composition, recording, or physical
execution is included.
