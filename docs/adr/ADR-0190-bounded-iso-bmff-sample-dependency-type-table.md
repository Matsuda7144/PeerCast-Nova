# ADR-0190: Bounded ISO BMFF Sample-dependency Type Table

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

The sample-dependency type (`sdtp`) box stores one byte per sample without an
internal entry count. Each byte contains three two-bit dependency values after
two reserved bits. Its length and values are untrusted, while the authoritative
sample count belongs to a separate sample-size table.

## Decision

Add a pure managed parser for one complete ISO-form `sdtp` box that:

- accepts normal, extended-size, and extends-to-end box headers;
- requires the declared size to match the supplied complete box;
- accepts FullBox version 0 with zero flags;
- derives the entry count from the remaining payload length;
- limits entries to 4,096 by default and 65,536 as a hard limit;
- requires the upper two reserved bits of every entry to be zero;
- accepts dependency values 0, 1, and 2 and rejects reserved value 3;
- projects the three fields through distinct enums in a read-only list; and
- accepts an empty table.

QuickTime-specific use of the upper bits is outside this ISO-only boundary.

## Consequences

Callers can inspect bounded dependency declarations without conflating the
three differently worded fields. Entry-count agreement remains a later
cross-table decision.

## Non-goals

No sample-count reconciliation, keyframe inference, packet flag projection,
seek policy, fragmented sample flags, decoder composition, recording, or
physical execution is included.
