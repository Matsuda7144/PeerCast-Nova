# ADR-0233: Bounded ISO BMFF Elementary Stream Descriptor

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0229 exposes the selected `mp4a` entry's exact child boxes and ADR-0231
defines one bounded MPEG-4 descriptor header. The `esds` FullBox still needs an
outer ES descriptor boundary before decoder-config and decoder-specific bodies
can be interpreted safely.

## Decision

Add a pure managed parser that:

- requires exactly one current `esds` child of the selected `mp4a` audio entry;
- requires FullBox version zero and zero flags;
- owns at most the existing 1 MiB codec-configuration limit;
- requires one exact outer ES descriptor tag `0x03`;
- projects the ES identifier, five-bit stream priority, and bounded optional
  dependency, URL, and OCR identifiers;
- defaults to 64 direct child descriptors with a 256 hard limit;
- requires a unique decoder-config descriptor followed by a unique SL-config
  descriptor while preserving other direct children; and
- projects child locations and validated payload views over the owned outer
  descriptor bytes.

## Consequences

Later parsers can interpret decoder-config, decoder-specific, and SL-config
bodies without repeating `esds` location, FullBox, outer-flag, ownership, or
direct-child traversal policy.

## Non-goals

No decoder-config body semantics, AAC AudioSpecificConfig, SL-config semantics,
demux event, decoder submission, native call, recording, or physical execution
is included.
