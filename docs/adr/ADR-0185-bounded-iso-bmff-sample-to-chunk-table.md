# ADR-0185: Bounded ISO BMFF Sample-to-chunk Table

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

The sample-to-chunk (`stsc`) box maps runs of chunks to a sample count and a
one-based sample-description index. Its entry count is untrusted, and malformed
ordering would make later chunk lookup ambiguous.

## Decision

Add a pure managed parser for one complete `stsc` box that:

- accepts normal, extended-size, and extends-to-end box headers;
- requires the declared size to match the supplied complete box;
- accepts FullBox version 0 with zero flags;
- limits entries to 4,096 by default and 65,536 as a hard limit;
- requires the payload length to match the entry count exactly;
- accepts an empty table;
- requires the first entry to start at chunk 1;
- requires subsequent `first_chunk` values to increase strictly;
- requires nonzero `samples_per_chunk` and `sample_description_index` values;
  and
- projects entries as a read-only list without expanding chunk or sample
  mappings.

## Consequences

Callers can inspect a bounded, structurally unambiguous sample-to-chunk run
table. Actual chunk ranges remain dependent on a separately parsed chunk-offset
table, so this parser does not infer a last chunk or total sample count.

## Non-goals

No `stco`, `co64`, sample lookup, chunk lookup, cross-table reconciliation,
fragmented runs, media-data reads, decoder composition, recording, or physical
execution is included.
