# ADR-0251: Bounded ISO BMFF Program Config Element Header

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0250 exposes the exact start of a Program Config Element when the Audio
Specific Config channel configuration is zero. The fixed leading fields and
bounded element counts should be projected independently before any variable
channel-element lists or comments are traversed.

## Decision

Add a pure managed fixed-header parser that:

- requires an ADR-0250 prefix whose channel configuration is zero;
- starts at that prefix's exact consumed bit position;
- projects element instance tag, object type, and sampling-frequency index;
- projects the front, side, back, LFE, associated-data, and valid-CC element
  counts directly from their bounded bit fields;
- rejects reserved sampling-frequency indices 13 through 15; and
- retains exact source provenance and the consumed bit position.

## Consequences

Variable PCE parsing can use validated fixed counts without guessing layout or
re-reading the GA prefix.

## Non-goals

Mixdown flags, channel-element records, byte alignment, comment bytes, channel
layout derivation, later GA fields, decoder configuration, native calls, and
physical execution are not included.
