# ADR-0241: Bounded ISO BMFF Implicit PS Sync Extension

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

An implicit SBR extension with SBR present can be followed immediately by the
optional `0x548` sync marker and one PS presence bit. ADR-0240 records the exact
ending position needed to parse that second marker without searching.

## Decision

Add a pure managed parser that:

- requires an ADR-0240 result with SBR present;
- treats insufficient marker bits or a different marker as not present;
- consumes only exact marker `0x548` and its one-bit PS presence flag; and
- retains source provenance and exact consumed bit position.

## Consequences

The supported implicit HE-AAC path can distinguish optional Parametric Stereo
signalling without conflating it with the primary SBR extension.

## Non-goals

No marker scan, PS decoder semantics, channel-layout inference, decoder
composition, native call, demux event, or physical execution is included.
