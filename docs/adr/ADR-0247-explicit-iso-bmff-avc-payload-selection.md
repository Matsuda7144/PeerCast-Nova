# ADR-0247: Explicit ISO BMFF AVC Payload Selection

## Status

Accepted as a post-0.0.10e non-physical codec-composition slice.

## Context

The bounded AVC path now exposes original length-prefixed, parameter-set-
prefixed length-prefixed, sample-only Annex-B, and parameter-set-prefixed
Annex-B payloads. Selection must not silently mix artifacts from different
samples or infer decoder policy.

## Decision

Add a pure managed selector that:

- requires the caller to name one of the four supported payload formats;
- uses the original owned candidate payload for the unchanged format;
- requires the corresponding materialized artifact for the other formats;
- requires every artifact to reference the exact same ADR-0242 classification;
- projects whether configuration parameter sets are included and whether the
  selected representation is Annex-B; and
- returns a non-copying payload view with source classification.

## Consequences

A later decoder or demux boundary can make an explicit format choice without
reimplementing source identity checks.

## Non-goals

No automatic choice by sample-entry kind, IDR state, decoder state, or runtime
capability; no decoder submission, native call, demux event, or physical
execution is included.
