# ADR-0177: ISO BMFF Movie-header Timing Projection

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

ADR-0176 provides the common FullBox prefix. The movie-header (`mvhd`) box is a
fixed-layout FullBox with version-dependent timing widths and is the smallest
useful movie-level metadata projection.

Creation and modification values use the ISO BMFF epoch, while duration uses
the declared timescale. Converting those values into wall-clock or managed time
types would introduce separate range and rounding policy.

## Decision

Add a pure managed parser for one complete `mvhd` box that:

- accepts normal, extended-size, and extends-to-end box headers;
- requires the declared size to match the supplied complete box;
- accepts only FullBox versions 0 and 1 with zero flags;
- requires the exact 100-byte version 0 or 112-byte version 1 payload;
- projects creation, modification, timescale, and duration as unsigned raw
  values with the correct version-dependent widths;
- rejects a zero timescale; and
- represents the version-specific all-ones duration sentinel as unknown.

No epoch conversion or duration division is performed.

## Consequences

Callers can inspect exact movie timing metadata without overflow-prone
conversion or payload retention.

The rest of the movie header remains structurally present but is not projected.

## Non-goals

No wall-clock conversion, duration rounding, rate, volume, matrix, next-track
policy, track metadata, sample tables, fragments, media-data association,
decoder composition, recording, or physical execution is included.
