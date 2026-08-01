# ADR-0211: Bounded ISO BMFF Media-time Conversion

## Status

Accepted as a post-0.0.10e non-physical timing-conversion slice.

## Context

ADR-0210 projects signed `Int128` presentation units, while `mdhd` supplies a
nonzero unsigned timescale. Conversion to managed ticks must not overflow
intermediate arithmetic and must apply an explicit output-magnitude policy.

## Decision

Add a pure managed converter that:

- accepts signed `Int128` media units and parsed `mdhd` timing;
- scales through `BigInteger` before division by the media timescale;
- truncates sub-tick fractions toward zero, matching the existing bounded
  demux timestamp conversion rule;
- defaults to a seven-day magnitude limit with a 3,650-day hard limit;
- accepts values exactly at the configured positive or negative limit; and
- distinguishes invalid configuration, missing timing, and excessive
  magnitude without projecting a partial value.

## Consequences

Callers can convert an exact ISO BMFF media-time value to a bounded `TimeSpan`
without narrowing the structural input before scaling.

## Non-goals

No interval-edge reconciliation, edit list, wall-clock conversion, packet
projection, payload read, decoder composition, recording, or physical
execution is included.
