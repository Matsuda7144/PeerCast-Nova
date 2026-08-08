# ADR-0259: Bounded ISO BMFF AAC Decoder Format Intent

## Status

Accepted as a post-0.0.10e non-physical decoder-composition prerequisite.

## Context

ADR-0258 provides conflict-free extension signalling and effective sampling
frequency. A final pure managed boundary can express intended decoder rate and
channel count while keeping capability checks and native configuration separate.

## Decision

Add a resolver that:

- retains the exact ADR-0258 facts;
- carries effective sampling frequency and core channel count unchanged when PS
  is absent;
- permits present PS only for a mono core and projects a stereo intent; and
- rejects PS with any non-mono core instead of guessing a downmix or layout.

## Consequences

Callers receive explicit effective rate and channel-count intent suitable for a
later decoder-capability boundary.

## Non-goals

Speaker masks, codec capability discovery, extradata materialization, decoder
open, resampling, native calls, and physical execution are not included.
