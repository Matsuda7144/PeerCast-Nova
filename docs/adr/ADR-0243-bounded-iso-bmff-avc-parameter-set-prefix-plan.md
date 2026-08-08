# ADR-0243: Bounded ISO BMFF AVC Parameter-set Prefix Plan

## Status

Accepted as a post-0.0.10e non-physical codec-composition slice.

## Context

ADR-0230 owns the `avcC` parameter sets and ADR-0242 classifies one exact
sample NAL sequence. Before any byte materialization, an explicit composition
boundary must prove that the configured parameter sets can use the selected
length-prefix width and remain inside packet limits.

## Decision

Add a pure managed prefix-plan builder that:

- requires a classified ADR-0242 sample and its already matched decoder
  configuration;
- requires at least one sequence and one picture parameter set;
- validates parameter-set kind against NAL types `7`, `8`, and `13`;
- requires every configured parameter-set length to fit the selected one-to-four
  byte prefix width;
- orders all configured parameter sets before the original sample NAL sequence;
- bounds total NAL count by 65,536 and projected encoded bytes by the existing
  4 MiB packet hard limit; and
- retains non-owning payload views and exact source classification.

## Consequences

Callers can inspect an exact, bounded parameter-set prefix plan before deciding
whether and how to materialize it.

## Non-goals

No automatic injection policy, duplicate suppression, byte materialization,
Annex-B conversion, decoder submission, native call, demux event, or physical
execution is included.
