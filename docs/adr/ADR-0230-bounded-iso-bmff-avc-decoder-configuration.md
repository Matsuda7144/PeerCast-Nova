# ADR-0230: Bounded ISO BMFF AVC Decoder Configuration

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Context

ADR-0228 exposes bounded child boxes for the selected `avc1` or `avc3` entry.
AVC packet framing and later decoder configuration require the unique `avcC`
record without trusting stale offsets or unbounded parameter-set allocation.

## Decision

Add a pure managed parser that:

- requires exactly one `avcC` child and revalidates its current box location;
- defaults to the existing 1 MiB codec-extradata hard limit;
- defaults to 256 parameter sets with a 4,096 hard limit;
- requires configuration version 1 and all reserved-bit patterns;
- projects the NAL length width and owned nonempty SPS／PPS sets;
- parses the defined high-profile chroma／bit-depth and SPS-extension suffix;
  and
- requires exact payload consumption with at least one SPS and PPS.

## Consequences

AVC framing and extradata composition can use stable owned configuration bytes
and parameter sets tied to the selected visual entry.

## Non-goals

No SPS semantic decoding, Annex-B conversion, keyframe inspection, demux event,
decoder submission, recording, or physical execution is included.
