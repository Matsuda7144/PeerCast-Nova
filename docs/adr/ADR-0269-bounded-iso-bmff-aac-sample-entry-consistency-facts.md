# ADR-0269: Bounded ISO BMFF AAC Sample Entry Consistency Facts

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

The parsed `mp4a` sample entry declares a 16.16 sample rate and channel count,
while ADR-0259 resolves effective AAC decoder facts from AudioSpecificConfig.
Production composition needs both views and their exact agreement state, but a
mismatch does not by itself define a safe admission policy for every AAC
extension profile.

## Decision

Add a pure managed projector that:

- requires an audio `mp4a` entry and one ADR-0259 decoder-format intent;
- retains exact identity of both sources;
- exposes declared fixed-point sample rate and channel count;
- exposes effective AAC sampling frequency and channel count;
- reports whether the declared rate is integral;
- reports sample-rate agreement and channel-count agreement separately; and
- reports exact agreement only when both dimensions match.

## Consequences

Later policy can distinguish container and codec metadata without reparsing or
silently choosing one source over the other.

## Non-goals

Mismatch admission or normalization policy, HE-AAC container convention
selection, speaker-layout binding, native decoder configuration, and physical
execution are not included.
