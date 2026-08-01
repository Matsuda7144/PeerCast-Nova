# ADR-0208: Bounded ISO BMFF Sample Decode-time Lookup

## Status

Accepted as a post-0.0.10e non-physical timing-lookup slice.

## Context

ADR-0182 projects bounded `stts` runs and checked aggregate sample and duration
units. A caller needs one sample's decode start and duration without expanding
every run into per-sample timestamps.

## Decision

Add a pure managed lookup that:

- requires the reconciled sample count to agree with `stts`;
- accepts a one-based sample number and rejects zero or values beyond the
  reconciled count;
- traverses at most the parser-bounded `stts` run list;
- accumulates only complete preceding runs and the target's run-relative
  offset; and
- projects decode start, unsigned 32-bit duration, and exclusive end in media
  time units without per-sample expansion.

The parser's checked total-duration invariant bounds all lookup arithmetic.

## Consequences

Callers can obtain exact decode-time units for one sample with work bounded by
the number of `stts` entries rather than the number of samples.

## Non-goals

No timescale conversion, `ctts` composition offset, presentation timestamp,
edit list, packet projection, payload read, decoder composition, recording, or
physical execution is included.
