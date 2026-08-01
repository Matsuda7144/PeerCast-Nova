# ADR-0181: Bounded ISO BMFF Sample-description Entry List

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

The sample-description (`stsd`) box declares the sample entries that identify
codec and format variants for a track. Before parsing AVC or AAC entry payloads,
the declared count and every entry range need a bounded structural boundary.

## Decision

Add a pure managed parser for one complete `stsd` box that:

- accepts normal, extended-size, and extends-to-end box headers;
- requires the declared size to match the supplied complete box;
- accepts only FullBox version 0 with zero flags;
- reads the unsigned entry count;
- limits entries to 16 by default and 256 as a hard limit;
- checks the limit before allocating the location projection;
- traverses each entry as an ISO BMFF box within the remaining `stsd` payload;
- exposes read-only entry locations relative to the complete `stsd` box; and
- requires the declared entry count to consume the payload exactly.

Entry payload bytes are not read, copied, retained, or interpreted.

## Consequences

Callers can enumerate bounded sample-entry types and exact ranges before
selecting a codec-specific parser.

This does not establish AVC, AAC, or any other sample-entry semantics.

## Non-goals

No visual/audio sample-entry parsing, codec configuration, data-reference
resolution, sample tables beyond `stsd`, fragments, media-data association,
decoder composition, recording, or physical execution is included.
