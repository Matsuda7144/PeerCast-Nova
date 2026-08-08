# ADR-0291: Bounded ISO BMFF Program Config Channel Reorder Plan

## Status

Accepted as a post-0.0.10e non-physical platform-metadata slice.

## Context

ADR-0290 identifies both PCE source order and WaveFormat platform order. A
consumer needs one validated permutation rather than independently interpreting
those indexes while processing decoded samples.

## Decision

Create a managed-only reorder plan from one exact ADR-0290 layout. Require a
nonempty, count-matched, source-identity-retaining permutation in which every
source index appears exactly once and every destination index is exact and
ordered. Retain the source layout and report whether reordering is required.

The plan contains metadata only and never reads, copies, or reorders sample
data.

## Consequences

Source-to-platform channel movement is explicit and validated before any frame
or native decoder boundary can consume it.

## Non-goals

Sample-buffer access, sample conversion, mixdown, native channel-layout writes,
decoder binding, device activation, and physical execution are not included.
