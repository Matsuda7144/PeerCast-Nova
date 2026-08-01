# ADR-0228: Bounded ISO BMFF Visual Sample Entry

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

ADR-0227 selects the exact `avc1` or `avc3` entry used by an admitted sample.
Codec configuration parsing first requires the common VisualSampleEntry fields
and a bounded child-box sequence.

## Decision

Add a pure managed parser that:

- accepts only a bound video `avc1` or `avc3` selection;
- reparses and matches the recorded entry location against current `stsd` bytes;
- requires the complete 78-byte VisualSampleEntry payload prefix;
- validates reserved and predefined fields, nonzero data-reference and
  dimensions, Pascal-name length, and terminal predefined value;
- projects raw dimensions, fixed-point resolutions, frame count, compressor
  bytes, and depth; and
- traverses child boxes exactly with a default 64 and hard 256 box limit.

## Consequences

AVC configuration lookup can operate on bounded absolute child locations tied
to the same sample-description selection.

## Non-goals

No `avcC` parsing, NAL conversion, pixel-aspect policy, codec extradata, demux
event, decoder submission, recording, or physical execution is included.
