# ADR-0183: Bounded ISO BMFF Composition-time Table

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

The composition time-to-sample (`ctts`) box run-length encodes offsets from
decode time to composition time. Version 0 offsets are unsigned, while version
1 offsets are signed. Treating both versions identically would lose either the
upper unsigned range or negative offsets.

## Decision

Add a pure managed parser for one complete `ctts` box that:

- accepts normal, extended-size, and extends-to-end box headers;
- requires the declared size to match the supplied complete box;
- accepts FullBox versions 0 and 1 with zero flags;
- limits entries to 4,096 by default and 65,536 as a hard limit;
- requires the payload length to match the declared entry count exactly;
- rejects zero sample counts;
- projects version 0 offsets as zero-extended unsigned 32-bit values in `long`;
- projects version 1 offsets as sign-extended signed 32-bit values in `long`;
- projects entries as a read-only list; and
- computes the total sample count.

The hard entry bound guarantees the total of 32-bit sample counts fits in
`ulong`.

## Consequences

Callers can inspect bounded composition-offset runs without expanding one entry
per sample and without confusing version-specific signedness.

Decode and composition runs are not joined in this slice.

## Non-goals

No timestamp expansion, `stts`/`ctts` reconciliation, edit lists, sample sizes,
chunk mapping, fragments, media-data association, decoder composition,
recording, or physical execution is included.
