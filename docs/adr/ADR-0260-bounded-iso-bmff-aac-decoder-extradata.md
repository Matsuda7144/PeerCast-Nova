# ADR-0260: Bounded ISO BMFF AAC Decoder Extradata

## Status

Accepted as a post-0.0.10e non-physical decoder-composition prerequisite.

## Context

ADR-0259 provides decoder-format intent anchored to the parsed Audio Specific
Config. A later native binding needs an explicitly owned, size-bounded copy of
those exact configuration bytes.

## Decision

Add a pure managed materializer that:

- requires the exact ADR-0259 format intent;
- obtains the original AudioSpecificConfig bytes through its retained source;
- rejects empty input and enforces configurable and hard codec-extradata limits;
- copies the exact bytes into private owned storage; and
- retains the format-intent provenance beside that copy.

## Consequences

AAC configuration bytes have an explicit managed ownership boundary suitable
for later decoder capability and native-padding decisions.

## Non-goals

FFmpeg input padding, codec capability discovery, native allocation, decoder
context mutation, decoder open, or physical execution are not included.
