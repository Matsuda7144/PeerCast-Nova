# ADR-0213: Bounded ISO BMFF Edit-list Parser

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

ADR-0212 provides unedited sample presentation intervals. The edit-list
(`elst`) box is the structural prerequisite for later mapping those media
times onto a movie timeline.

## Decision

Add a pure managed parser for one complete `elst` box that:

- accepts normal, extended-size, and extends-to-end headers;
- requires FullBox version 0 or 1 with zero flags and exact payload length;
- defaults to 4,096 entries with a 65,536-entry hard limit;
- projects version 0 32-bit and version 1 64-bit duration and signed media-time
  widths without narrowing;
- retains both signed 16-bit media-rate components without applying rate
  policy;
- identifies `media_time == -1` as an empty edit; and
- computes the total segment duration with checked unsigned 64-bit arithmetic.

## Consequences

Callers can inspect bounded edit-list structure without prematurely deciding
which rates or edit patterns are supported.

## Non-goals

No edit application, movie/media timescale conversion, dwell or rate policy,
packet projection, payload read, decoder composition, recording, or physical
execution is included.
