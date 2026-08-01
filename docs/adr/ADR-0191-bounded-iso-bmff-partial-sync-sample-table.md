# ADR-0191: Bounded ISO BMFF Partial-sync Sample Table

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

The partial-sync sample (`stps`) box lists one-based sample numbers that offer
partial rather than full random access. Its entry count is untrusted, and
malformed ordering would make later lookup ambiguous.

## Decision

Add a pure managed parser for one complete `stps` box that:

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

Callers can inspect a bounded, ordered partial-sync declaration independently
from full sync samples. Cross-table meaning remains a later decision.

## Non-goals

No `stss` integration, sample-count reconciliation, seek policy, packet flag
projection, fragmented random-access metadata, decoder composition, recording,
or physical execution is included.
