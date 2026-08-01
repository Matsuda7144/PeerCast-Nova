# ADR-0229: Bounded ISO BMFF Audio Sample Entry

## Status

Accepted as a post-0.0.10e non-physical parser slice.

## Context

ADR-0227 selects the exact `mp4a` entry used by an admitted audio sample. MPEG-4
audio configuration lookup requires the common version-zero AudioSampleEntry
fields and a bounded child-box sequence.

## Decision

Add a pure managed parser that:

- accepts only a bound audio `mp4a` selection;
- reparses and matches the recorded entry location against current `stsd` bytes;
- requires the complete 28-byte version-zero AudioSampleEntry payload prefix;
- validates reserved fields, nonzero data reference, channel count, sample size,
  and integer sample-rate component;
- preserves channel count, sample size, and the raw 16.16 sample rate; and
- traverses child boxes exactly with a default 64 and hard 256 box limit.

## Consequences

MPEG-4 audio configuration lookup can operate on bounded absolute child
locations tied to the same sample-description selection.

## Non-goals

No QuickTime sound-version extensions, `esds` parsing, AAC configuration,
channel-layout policy, demux event, decoder submission, recording, or physical
execution is included.
