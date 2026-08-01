# ADR-0198: ISO BMFF Chunk Sample-count Reconciliation

## Status

Accepted as a post-0.0.10e non-physical chunk-count slice.

## Context

ADR-0197 establishes valid `stsc` coverage over a known chunk count. Those run
boundaries and `samples_per_chunk` values are sufficient to calculate the
sample count represented by the chunk table without expanding each chunk.

## Decision

Extend the pure managed chunk-table reconciler so that:

- each run contributes its chunk span multiplied by `samples_per_chunk`;
- the total is accumulated as an unsigned 64-bit value;
- successful output always projects the calculated total sample count;
- a new overload optionally requires exact agreement with a supplied unsigned
  32-bit reconciled sample count;
- mismatch returns a distinct fail-closed result; and
- the existing overload retains projection-only behavior.

The parser limits chunk and run counts to 65,536, so the maximum product of
chunk count and unsigned 32-bit `samples_per_chunk` fits in 64 bits.

## Consequences

Callers can verify that timing, size, and chunk tables describe the same number
of samples without materializing a per-chunk map.

## Non-goals

No per-chunk or per-sample expansion, sample-description validation, offset
ordering or media-data bounds, lookup, decoder composition, recording, or
physical execution is included.
