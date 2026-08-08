# ADR-0264: Bounded ISO BMFF Program Config Channel Slot Map

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0253 validates PCE element identities and channel counts. Decoder-facing
composition also needs a stable channel-slot sequence, but the parsed syntax
does not by itself justify assigning platform speaker positions such as front
left, front right, or center.

## Decision

Add a pure managed mapper that:

- requires one exact ADR-0253 topology and retains its source identity;
- emits slots in PCE region order: front, side, back, then LFE;
- preserves the element index and tag within each region;
- expands SCE as one `SingleChannel` slot, CPE as ordered `PairFirst` and
  `PairSecond` slots, and each LFE element as one `Lfe` slot;
- assigns a contiguous zero-based channel index; and
- rejects a topology whose declared total disagrees with the emitted slots.

## Consequences

Later decoder-layout policy can consume a finite, source-bound channel sequence
without reparsing the PCE or conflating syntactic order with speaker position.

## Non-goals

Left/right/center speaker assignment, platform speaker-mask derivation, mixdown
application, decoder binding, native calls, and physical execution are not
included.
