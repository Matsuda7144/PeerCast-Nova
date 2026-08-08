# ADR-0272: Explicit ISO BMFF Program Config Speaker-Position Assignment

## Status

Accepted as a post-0.0.10e non-physical codec-metadata policy slice.

## Context

ADR-0264 provides the exact ordered channel slots from a validated Program Config
Element but intentionally does not infer physical speaker positions. A later
decoder-layout boundary needs reviewable caller intent rather than an implicit
mapping from PCE region or pair order.

## Decision

Add a pure managed assigner that:

- requires one exact ADR-0264 slot map and one caller-provided position for every
  slot;
- supports a finite set of front, side, back, center, and low-frequency position
  values;
- rejects missing inputs, count mismatch, undefined values, and duplicate
  positions;
- requires a `LowFrequency` position exactly where the source slot is an LFE
  component; and
- returns ordered bindings that retain the exact slot-map and slot identities.

No default position sequence is supplied. PCE regions and pair components never
select a position by themselves.

## Consequences

Speaker intent becomes explicit, finite, and source-bound before any platform or
decoder layout representation is considered.

## Non-goals

Canonical-layout inference, platform speaker-mask derivation, channel reordering,
mixdown application, FFmpeg channel-layout transfer, decoder binding, native
calls, and physical execution are not included.
