# ADR-0179: ISO BMFF Handler-reference Classification

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

After `mdhd` timing, the handler-reference (`hdlr`) box identifies whether the
media track carries video, audio, or another handler role. Its variable UTF-8
name is untrusted and must not create an unbounded projection.

## Decision

Add a pure managed parser for one complete `hdlr` box that:

- accepts normal, extended-size, and extends-to-end box headers;
- requires the declared size to match the supplied complete box;
- accepts only FullBox version 0 with zero flags;
- requires zero `pre_defined` and three zero reserved fields;
- projects the raw handler type;
- classifies `vide` as Video, `soun` as Audio, and every other value as Other;
- requires one final null terminator with no embedded null;
- decodes the name as strict UTF-8;
- limits name bytes to 255 by default and 4,096 as a hard limit; and
- checks the byte limit before decoding.

An empty null-terminated name is valid.

## Consequences

Callers can classify a media handler and display a bounded immutable name
without retaining the source box.

Classification does not choose a codec or sample entry.

## Non-goals

No handler allowlist beyond Video/Audio classification, sample-description
parsing, codec selection, sample tables, fragments, media-data association,
decoder composition, recording, or physical execution is included.
