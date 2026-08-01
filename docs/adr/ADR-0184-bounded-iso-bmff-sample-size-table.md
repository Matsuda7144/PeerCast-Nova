# ADR-0184: Bounded ISO BMFF Sample-size Table

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

The sample size (`stsz`) box either declares one size shared by every sample or
stores one 32-bit size for each sample. Expanding the uniform form would permit
an untrusted sample count to drive unnecessary allocation, while the per-sample
form needs an explicit projection bound.

## Decision

Add a pure managed parser for one complete `stsz` box that:

- accepts normal, extended-size, and extends-to-end box headers;
- requires the declared size to match the supplied complete box;
- accepts FullBox version 0 with zero flags;
- preserves the default sample size and sample count as unsigned 32-bit values;
- projects no entries when the default sample size is nonzero;
- limits individual-size entries to 4,096 by default and 65,536 as a hard
  limit;
- requires the payload length to match the selected uniform or individual form
  exactly;
- preserves individual zero sizes without adding a container-level validity
  policy;
- projects individual sizes as a read-only list; and
- computes exact total sample bytes in `ulong`.

The product of two unsigned 32-bit values fits in `ulong`. The hard individual
entry bound likewise guarantees that the sum of projected 32-bit sizes fits.

## Consequences

Callers can inspect exact sample byte sizes with bounded allocation. Uniform
tables retain their full 32-bit sample count without expansion.

Sample-to-chunk mapping and media-data association are not inferred.

## Non-goals

No `stsc`, `stco`, `co64`, sample lookup, chunk lookup, fragmented runs,
media-data reads, decoder composition, recording, or physical execution is
included.
