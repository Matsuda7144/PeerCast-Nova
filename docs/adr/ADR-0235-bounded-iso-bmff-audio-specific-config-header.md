# ADR-0235: Bounded ISO BMFF Audio Specific Config Header

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0234 identifies one nonempty decoder-specific-info payload for MPEG-4 audio.
Its leading AudioSpecificConfig fields must be decoded at bit precision before
later AAC profile and channel semantics can be selected.

## Decision

Add a pure managed header parser that:

- requires MPEG-4 audio object-type indication `0x40`;
- uses only the decoder-specific-info location owned by ADR-0234;
- bounds configuration bytes by the existing 1 MiB codec-extradata hard limit;
- parses normal and extended Audio Object Type encodings;
- parses indexed sampling frequencies and nonzero explicit 24-bit frequencies;
- rejects reserved sampling-frequency indices; and
- projects channel configuration and exact consumed header bits while retaining
  the owned configuration bytes.

## Consequences

Later AAC-specific parsing can start from exact object type, frequency, channel
configuration, and bit position without duplicating bit arithmetic.

## Non-goals

No SBR or PS extension semantics, GASpecificConfig, Program Config Element,
channel-layout resolution, decoder extradata composition, native call, demux
event, or physical execution is included.
