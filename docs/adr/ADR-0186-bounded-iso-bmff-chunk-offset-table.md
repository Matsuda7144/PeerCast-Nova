# ADR-0186: Bounded ISO BMFF Chunk-offset Table

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

The 32-bit chunk-offset (`stco`) box stores one absolute file offset for each
media chunk. Its entry count is untrusted, while offset validity depends on
container and media-data ranges that are not yet composed.

## Decision

Add a pure managed parser for one complete `stco` box that:

- accepts normal, extended-size, and extends-to-end box headers;
- requires the declared size to match the supplied complete box;
- accepts FullBox version 0 with zero flags;
- limits entries to 4,096 by default and 65,536 as a hard limit;
- requires the payload length to match the entry count exactly;
- accepts an empty table;
- preserves every offset as an unsigned 32-bit value, including zero and
  non-monotonic values; and
- projects offsets as a read-only list.

This parser does not impose an ordering or media-data-range policy because
validity of an absolute offset requires container-level context.

## Consequences

Callers can inspect bounded 32-bit chunk offsets without performing file or
media-data access. Offset admission remains a later composition decision.

## Non-goals

No `co64`, offset dereference, media-data range validation, `stsc`
reconciliation, sample or chunk lookup, fragmented runs, decoder composition,
recording, or physical execution is included.
