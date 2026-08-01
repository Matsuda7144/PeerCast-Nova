# ADR-0203: Bounded ISO BMFF Sample-size Lookup

## Status

Accepted as a post-0.0.10e non-physical sample-size lookup slice.

## Context

The reconciled sample projection identifies the authoritative `stsz` or
`stz2` table, sample count, and total bytes. A caller needs to retain those
decisions before indexing one sample size.

## Decision

Add a pure managed lookup that:

- accepts the reconciled sample projection and parsed size tables;
- requires exactly the table selected by the reconciled size-table kind;
- requires sample count and total bytes to match the reconciliation;
- rejects zero and greater-than-count one-based sample numbers;
- returns an `stsz` default size directly without expanding a table; and
- projects individual `stsz` and compact `stz2` entries as unsigned 32-bit
  sizes.

## Consequences

Callers can retrieve one authoritative sample size while detecting a table
substitution or inconsistent reconciliation.

## Non-goals

No chunk-local size accumulation, sample byte-offset or extent calculation,
media-data bounds or reads, codec selection, decoder composition, recording,
or physical execution is included.
