# ADR-0210: Exact ISO BMFF Sample Presentation-time Lookup

## Status

Accepted as a post-0.0.10e non-physical timing-composition slice.

## Context

ADR-0208 projects unsigned 64-bit decode timing and ADR-0209 projects a signed
composition offset. Their sum may be negative or may exceed `long.MaxValue`,
so narrowing to either signed or unsigned 64-bit representation would reject
valid structural values.

## Decision

Add a pure managed composed lookup that:

- reuses the decode-time and optional composition-offset lookups for the same
  one-based sample;
- propagates sample bounds and fails closed on either reconciliation error;
- adds the unsigned decode start and signed composition offset as `Int128`;
- preserves the decode duration and projects an `Int128` exclusive end; and
- retains the offset value and whether it came from an explicit `ctts` entry.

## Consequences

Callers can obtain an exact presentation interval in media time units without
losing negative times or structurally valid positive values beyond signed
64-bit range.

## Non-goals

No timescale conversion, duration policy, edit list, timestamp magnitude
limit, packet projection, payload read, decoder composition, recording, or
physical execution is included.
