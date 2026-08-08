# ADR-0257: Bounded ISO BMFF AAC Core Format Facts

## Status

Accepted as a post-0.0.10e non-physical decoder-composition prerequisite.

## Context

GA parsing now exposes exact source metadata, while ADR-0253 provides PCE
channel topology. Decoder composition needs a single bounded projection of the
declared sampling frequency and core channel count without selecting SBR output
rate or platform speaker positions.

## Decision

Add a pure managed resolver that:

- retains the exact GA Specific Config source;
- maps channel configurations one through seven to 1, 2, 3, 4, 5, 6, and 8
  channels respectively;
- requires exact source-bound PCE topology for channel configuration zero;
- rejects unexpected topology, reserved indexed configurations, and zero-channel
  PCE topology; and
- labels the header rate as declared sampling frequency, not final decoder
  output frequency.

## Consequences

Core AAC object type, declared rate, and channel count are available without
conflating PCE topology or SBR signalling with decoder output policy.

## Non-goals

Final SBR/PS rate selection, speaker masks, extradata materialization, decoder
open, native calls, and physical execution are not included.
