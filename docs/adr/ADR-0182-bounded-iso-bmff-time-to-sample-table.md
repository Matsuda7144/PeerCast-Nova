# ADR-0182: Bounded ISO BMFF Time-to-sample Table

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

The decoding time-to-sample (`stts`) box run-length encodes sample counts and
decode-duration deltas. Both its entry count and derived totals come from
untrusted input and need explicit bounds and overflow behavior.

## Decision

Add a pure managed parser for one complete `stts` box that:

- accepts normal, extended-size, and extends-to-end box headers;
- requires the declared size to match the supplied complete box;
- accepts only FullBox version 0 with zero flags;
- limits entries to 4,096 by default and 65,536 as a hard limit;
- requires the payload length to match the declared entry count exactly;
- rejects zero sample counts while retaining zero sample deltas;
- projects entries as a read-only list; and
- computes total sample count and total duration units with checked unsigned
  arithmetic and a fixed overflow result.

## Consequences

Callers can inspect bounded decode-time runs and exact aggregate units without
expanding the runs into one item per sample.

The totals remain in the media timescale and are not converted or rounded.

## Non-goals

No timestamp expansion, composition offsets, sync samples, sample sizes, chunk
mapping, fragments, media-data association, decoder composition, recording, or
physical execution is included.
