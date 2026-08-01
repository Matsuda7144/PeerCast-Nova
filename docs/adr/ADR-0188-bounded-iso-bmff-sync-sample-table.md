# ADR-0188: Bounded ISO BMFF Sync-sample Table

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

The sync-sample (`stss`) box identifies random-access samples by one-based
sample number. Its entry count is untrusted, and zero, duplicate, or reversed
numbers would violate the ordered table contract.

## Decision

Add a pure managed parser for one complete `stss` box that:

- accepts normal, extended-size, and extends-to-end box headers;
- requires the declared size to match the supplied complete box;
- accepts FullBox version 0 with zero flags;
- limits entries to 4,096 by default and 65,536 as a hard limit;
- requires the payload length to match the entry count exactly;
- accepts an empty table;
- requires every sample number to be nonzero;
- requires sample numbers to increase strictly; and
- projects sample numbers as a read-only unsigned 32-bit list.

## Consequences

Callers can inspect a bounded and unambiguous list of declared sync samples.
Whether a number exists in the complete sample table remains a later
cross-table validation decision.

Absence of an `stss` box has separate all-samples-sync semantics and is not
represented by parsing an empty box.

## Non-goals

No sample-count reconciliation, keyframe seek, packet keyframe projection,
sample lookup, fragmented random-access metadata, decoder composition,
recording, or physical execution is included.
