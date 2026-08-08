# ADR-0270: Explicit ISO BMFF AAC Sample Entry Consistency Policy

## Status

Accepted as a post-0.0.10e non-physical decoder-composition prerequisite.

## Context

ADR-0269 reports `mp4a` declaration agreement without choosing an admission
policy. A caller may require exact agreement or may explicitly choose the AAC
effective decoder facts for a known profile-specific mismatch. That choice must
not be implicit.

## Decision

Add a pure managed resolver with two explicit policies:

- `RequireExactAgreement` admits only facts whose rate and channel count both
  match; and
- `AllowEffectiveDecoderFacts` admits the AAC effective sampling frequency and
  channel count even when the sample-entry declaration differs.

The decision retains the exact ADR-0269 facts and policy, exposes decoder values,
and records whether mismatch override was actually used. Missing facts and
undefined policy values fail without a decision.

## Consequences

Mismatch admission becomes reviewable caller intent rather than an implicit
normalization side effect.

## Non-goals

No policy is selected by default. Profile-specific policy selection, rewriting
the sample entry, speaker-layout binding, native decoder configuration, and
physical execution are not included.
