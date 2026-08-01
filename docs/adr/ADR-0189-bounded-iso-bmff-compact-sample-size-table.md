# ADR-0189: Bounded ISO BMFF Compact Sample-size Table

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

The compact sample-size (`stz2`) box stores one size per sample using packed
4-, 8-, or 16-bit fields. Its sample count is untrusted, and packed input needs
an exact byte-length rule before allocation and projection.

## Decision

Add a pure managed parser for one complete `stz2` box that:

- accepts normal, extended-size, and extends-to-end box headers;
- requires the declared size to match the supplied complete box;
- accepts FullBox version 0 with zero flags and zero reserved fields;
- accepts only field sizes 4, 8, and 16;
- limits samples to 4,096 by default and 65,536 as a hard limit;
- requires the packed payload length to match the sample count exactly;
- reads the high nibble first for 4-bit entries and ignores the unused low
  nibble for an odd sample count;
- preserves zero sizes and projects sizes as a read-only unsigned 16-bit list;
  and
- computes exact total sample bytes in `ulong`.

## Consequences

Callers can inspect bounded compact sample sizes without retaining packed input
or expanding beyond the explicit sample-count limit.

## Non-goals

No `stsz`/`stz2` selection, sample-table reconciliation, sample lookup,
media-data reads, fragmented runs, decoder composition, recording, or physical
execution is included.
