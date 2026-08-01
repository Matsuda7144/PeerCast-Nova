# ADR-0180: ISO BMFF Track-header Identity Projection

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

The track-header (`tkhd`) box provides stable track identity, movie-timeline
duration, lifecycle flags, and presentation dimensions. Its time fields vary by
version, while width and height are unsigned 16.16 fixed-point values.

Matrix interpretation and conversion of fixed-point dimensions are separate
presentation policies.

## Decision

Add a pure managed parser for one complete `tkhd` box that:

- accepts normal, extended-size, and extends-to-end box headers;
- requires the declared size to match the supplied complete box;
- accepts FullBox versions 0 and 1;
- accepts only the four defined low flag bits and projects each meaning;
- requires the exact 84-byte version 0 or 96-byte version 1 payload;
- projects creation, modification, nonzero track ID, and duration with the
  correct unsigned widths;
- represents the version-specific all-ones duration sentinel as unknown;
- requires both reserved time-adjacent fields and the final reserved field to
  be zero; and
- projects width and height as raw unsigned 16.16 fixed-point values.

Layer, alternate group, volume, and matrix bytes remain unprojected.

## Consequences

Callers can correlate track identity with handler and media timing metadata
without prematurely interpreting presentation transforms.

## Non-goals

No matrix validation, rotation, pixel conversion, edit lists, handler/track
composition, sample descriptions, sample tables, fragments, decoder
composition, recording, or physical execution is included.
