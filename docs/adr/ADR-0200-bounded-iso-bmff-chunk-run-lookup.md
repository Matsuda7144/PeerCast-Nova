# ADR-0200: Bounded ISO BMFF Chunk-run Lookup

## Status

Accepted as a post-0.0.10e non-physical chunk-lookup slice.

## Context

ADR-0197 through ADR-0199 establish bounded `stsc` coverage, total sample
count, and optional sample-description agreement. A caller still needs a
bounded way to locate the run governing one chunk without expanding every
chunk.

## Decision

Add a pure managed lookup that:

- accepts a parsed `stsc` table and reconciled chunk-table projection;
- revalidates run count, empty/nonempty coverage, final run boundary, total
  sample count, and optional description count;
- rejects zero or greater-than-count one-based chunk numbers;
- uses binary search over strictly increasing `first_chunk` values; and
- projects the zero-based run index, inclusive chunk range,
  `samples_per_chunk`, and `sample_description_index`.

## Consequences

Callers can identify a chunk's governing run in logarithmic search time without
allocating a per-chunk table or reading offsets and media data.

## Non-goals

No per-sample lookup, byte-offset resolution, sample-size accumulation,
media-data bounds, codec selection, decoder composition, recording, or
physical execution is included.
