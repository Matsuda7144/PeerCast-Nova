# ADR-0234: Bounded ISO BMFF Decoder Config Descriptor

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0233 owns the outer ES descriptor and identifies its unique decoder-config
child. The decoder-config body and nested decoder-specific descriptor still
need exact bounded semantics before AAC configuration can be projected.

## Decision

Add a pure managed parser that:

- consumes only the decoder-config child owned by the parsed ES descriptor;
- validates the fixed 13-byte body and required reserved bit;
- requires MPEG-4 audio stream type `0x05` for the selected `mp4a` entry;
- projects object-type indication, upstream flag, 24-bit buffer size, and
  maximum and average bitrates;
- defaults to 64 nested descriptors with a 256 hard limit;
- bounds nested payloads by the existing 1 MiB codec-extradata hard limit; and
- requires exactly one nonempty decoder-specific-info descriptor while
  preserving other nested descriptors.

## Consequences

AAC configuration parsing can use a validated decoder-specific owned view tied
to the exact selected `esds` hierarchy.

## Non-goals

No AudioSpecificConfig semantics, SL-config semantics, bitrate enforcement,
demux event, decoder submission, native call, recording, or physical execution
is included.
