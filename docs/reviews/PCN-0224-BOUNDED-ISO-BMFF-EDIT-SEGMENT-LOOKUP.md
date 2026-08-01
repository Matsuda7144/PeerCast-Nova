# PCN-0224: Bounded ISO BMFF Edit-segment Lookup

## Scope

Review the pure managed lookup from one movie-timeline unit into a parsed
`elst` entry.

## Findings

- Missing and empty edit lists fail closed.
- Segment and table ends use exclusive bounds.
- Zero-duration entries cannot capture a movie time.
- The result retains the entry index, exact entry, absolute movie bounds, and
  segment-relative offset.
- Empty edits are identified but no edit policy is applied.
- Work is limited by the parser's bounded entry count and performs no native or
  physical operation.

## Decision

Accept the bounded edit-segment lookup. Movie/media timescale and rate-aware
application remain incomplete.
