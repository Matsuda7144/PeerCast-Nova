# ADR-0204: Bounded ISO BMFF Sample Byte-range Lookup

## Status

Accepted as a post-0.0.10e non-physical byte-range slice.

## Context

ADR-0201 through ADR-0203 provide validated chunk offsets, sample-to-chunk
locations, and sample sizes. These values can define one sample's absolute
byte range after adding the sizes of preceding samples in the same chunk.

## Decision

Add a pure managed composed lookup that:

- requires chunk and sample reconciliations to agree on total sample count;
- reuses the bounded sample-to-chunk, chunk-offset, and sample-size lookups;
- uses multiplication for unexpanded default `stsz` sizes;
- accumulates only preceding entries in the same chunk for individual `stsz`
  or `stz2` tables;
- limits individual accumulation through the parser's 65,536-sample bound;
- detects overflow while adding the chunk offset, preceding bytes, and target
  size; and
- projects absolute offset, unsigned 32-bit size, and exclusive end offset.

## Consequences

Callers can identify a structural sample byte range without reading the file or
materializing per-sample offsets.

## Non-goals

No file-length or `mdat` bounds, byte reads, offset ordering, fragmented runs,
codec selection, decoder composition, recording, or physical execution is
included.
