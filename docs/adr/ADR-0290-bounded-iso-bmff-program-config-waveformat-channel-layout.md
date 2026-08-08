# ADR-0290: Bounded ISO BMFF Program Config WaveFormat Channel Layout

## Status

Accepted as a post-0.0.10e non-physical platform-metadata slice.

## Context

ADR-0272 retains explicit caller-assigned speaker positions in PCE source order.
Windows `WAVEFORMATEXTENSIBLE` uses defined speaker-mask bits and requires its
channels in ascending speaker-bit order. The source order must not be silently
treated as that platform order.

## Decision

Project the finite ADR-0272 position set to the corresponding WaveFormat speaker
mask bits. Retain the exact assignment and produce bindings in ascending mask-bit
order, with both source and platform channel indexes. Reject missing,
inconsistent, duplicate, or unsupported input without producing a layout.

The projection describes a reorder requirement but does not reorder media data.

## Consequences

PCE speaker intent now has a bounded, reviewable Windows channel-mask and
source-to-platform permutation before any decoder or audio-device integration.

## Non-goals

Default speaker inference, sample reordering, mixdown, native structure writes,
decoder binding, device activation, and physical execution are not included.
