# ADR-0202: Bounded ISO BMFF Sample-to-chunk Lookup

## Status

Accepted as a post-0.0.10e non-physical sample-lookup slice.

## Context

The reconciled `stsc` runs establish total sample count and chunk coverage. A
caller still needs to locate a one-based sample within those runs without
expanding a per-chunk or per-sample table.

## Decision

Add a pure managed lookup that:

- accepts a parsed `stsc` table and reconciled chunk-table projection;
- delegates reconciliation validation to the bounded chunk-run lookup;
- accepts an unsigned 64-bit one-based sample number;
- rejects zero and numbers greater than the reconciled total sample count;
- advances only across bounded `stsc` runs;
- uses division and remainder within the matching run; and
- projects run index, chunk number, first sample number in the chunk,
  zero-based chunk-local sample index, samples per chunk, and sample-description
  index.

## Consequences

Callers can map a sample to its structural chunk location without allocating
per-chunk or per-sample mappings.

## Non-goals

No sample byte-offset calculation, sample-size lookup, chunk byte extent,
media-data bounds or reads, codec selection, decoder composition, recording,
or physical execution is included.
