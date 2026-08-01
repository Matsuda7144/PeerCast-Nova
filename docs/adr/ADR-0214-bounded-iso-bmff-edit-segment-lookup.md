# ADR-0214: Bounded ISO BMFF Edit-segment Lookup

## Status

Accepted as a post-0.0.10e non-physical lookup slice.

## Context

ADR-0213 projects a bounded `elst` table and its checked total movie duration.
Later edit application first needs an exact answer to which edit contains one
movie-timeline unit.

## Decision

Add a pure managed lookup that:

- accepts one unsigned movie-timeline unit and a parsed edit list;
- treats segment ends and the total table end as exclusive;
- skips zero-duration entries naturally;
- returns the entry index, entry value, absolute movie bounds, and relative
  segment offset; and
- reports empty edits without applying media-rate or timescale policy.

The lookup is bounded by the parser's existing entry-count limit. Its running
addition is checked even though a parser-produced table already has a checked
aggregate.

## Consequences

Callers can locate edit segments without expanding the table or conflating
structural lookup with edit application.

## Non-goals

No movie/media timescale conversion, media-rate validation, dwell handling,
sample selection, payload read, decoder composition, recording, or physical
execution is included.
