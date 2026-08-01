# ADR-0187: Bounded ISO BMFF Large Chunk-offset Table

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

The large chunk-offset (`co64`) box stores one unsigned 64-bit absolute file
offset for each media chunk. ADR-0186 covers only the 32-bit `stco` form.
Entry count and allocation still require an explicit bound.

## Decision

Add a pure managed parser for one complete `co64` box that:

- accepts normal, extended-size, and extends-to-end box headers;
- requires the declared size to match the supplied complete box;
- accepts FullBox version 0 with zero flags;
- limits entries to 4,096 by default and 65,536 as a hard limit;
- requires the payload length to match the entry count exactly;
- accepts an empty table;
- preserves every offset as an unsigned 64-bit value, including zero and
  non-monotonic values; and
- projects offsets as a read-only list.

This parser does not impose ordering, media-data-range, or `stco` versus `co64`
selection policy because those require container-level context.

## Consequences

Callers can inspect bounded 64-bit chunk offsets without truncation or file
access. Offset and table selection admission remain later composition
decisions.

## Non-goals

No offset dereference, media-data range validation, `stco`/`co64` selection,
`stsc` reconciliation, sample or chunk lookup, fragmented runs, decoder
composition, recording, or physical execution is included.
