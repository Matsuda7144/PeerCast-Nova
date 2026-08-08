# ADR-0253: Bounded ISO BMFF Program Config Channel Topology

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0252 exposes all syntactic PCE element records. A bounded semantic step can
count the channels represented by SCE, CPE, and LFE records and reject duplicate
element identities without guessing a platform speaker mask.

## Decision

Add a pure managed projector that:

- retains exact ADR-0252 source identity;
- counts SCE as one channel, CPE as two, and each LFE record as one;
- projects front, side, back, core, LFE, and total channel counts; and
- rejects duplicate channel identities by element kind and tag, plus duplicate
  LFE, associated-data, or coupling tags with distinct results.

## Consequences

Decoder composition can consume an exact bounded channel count while speaker
position mapping remains an explicit later policy decision.

## Non-goals

Speaker-mask derivation, ordering policy, mixdown application, decoder binding,
native calls, and physical execution are not included.
