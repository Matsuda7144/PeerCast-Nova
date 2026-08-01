# ADR-0197: ISO BMFF Chunk-table Reconciliation

## Status

Accepted as a post-0.0.10e non-physical chunk-table slice.

## Context

`stsc` run boundaries are meaningful only with the chunk count supplied by
exactly one `stco` or `co64` table. The individual parsers cannot establish
that cross-table coverage.

## Decision

Add a pure managed chunk-table reconciler that:

- requires a parsed `stsc` table;
- requires exactly one parsed `stco` or `co64` table;
- accepts empty `stsc` and offset tables only when both are empty;
- requires a nonempty `stsc` table for nonempty chunks;
- requires the final `first_chunk` run boundary not to exceed the chunk count;
- returns a distinct fail-closed result for each missing, conflicting, or
  coverage condition; and
- projects the chunk count, run count, and offset-table kind.

Because the `stsc` parser guarantees the first run starts at chunk 1 and run
boundaries increase strictly, checking the final boundary establishes that all
run starts refer to existing chunks.

## Consequences

Callers can establish bounded chunk-run coverage without expanding mappings or
reading media data.

## Non-goals

No total sample-count calculation, per-chunk expansion, sample-description
validation, offset ordering or media-data bounds, lookup, decoder composition,
recording, or physical execution is included.
