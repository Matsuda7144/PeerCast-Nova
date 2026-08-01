# ADR-0201: Bounded ISO BMFF Chunk-offset Lookup

## Status

Accepted as a post-0.0.10e non-physical offset-lookup slice.

## Context

The reconciled chunk table identifies whether `stco` or `co64` is
authoritative and projects its chunk count. A caller needs a bounded lookup
that preserves those decisions before indexing an offset table.

## Decision

Add a pure managed lookup that:

- accepts a reconciled chunk-table projection and parsed offset tables;
- requires exactly the table selected by the reconciled offset kind;
- requires the selected table's entry count to equal the reconciled chunk
  count;
- rejects zero and greater-than-count one-based chunk numbers;
- converts the validated chunk number to a zero-based index only after those
  checks; and
- projects both 32-bit and 64-bit offsets as unsigned 64-bit values.

## Consequences

Callers can retrieve an authoritative chunk offset without conflating `stco`
and `co64` or indexing an inconsistent table.

## Non-goals

No offset ordering rule, file-length or media-data bounds, byte reads,
per-sample offset calculation, decoder composition, recording, or physical
execution is included.
