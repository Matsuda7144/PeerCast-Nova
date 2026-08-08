# ADR-0300: Bounded ISO BMFF Codec Packet Projection

## Status

Accepted as a post-0.0.10e non-physical managed-demux metadata slice.

## Context

The edited-sample packet candidate owns exact bounded sample bytes and timing.
The selected sample description identifies `avc1`, `avc3`, or `mp4a`. The AVC
path can additionally select the exact length-prefixed or Annex-B payload form,
while AAC retains its original compressed sample bytes. These facts were not
yet exposed behind one codec-neutral packet boundary.

## Decision

Project either an existing AVC payload composition or an AAC packet candidate
plus exact audio sample entry into one codec-neutral packet result. Retain the
exact edited-sample candidate, sample-description selection, codec and media
classification, codec payload bytes, and the exact codec-specific source.

For AVC, use only the payload selected by the existing bounded AVC composition.
For AAC, use only the candidate's original owned payload. Require the candidate
and sample entry to retain the same nonzero sample-description index and reject
unsupported codec/media pairs, missing inputs, mismatched descriptions, or an
empty payload.

## Consequences

Callers can consume one managed packet shape for supported AVC and AAC samples
without discarding exact codec-specific provenance or edited timing.

## Non-goals

Decoder preparation ownership, stream identity, demux-event scheduling,
multi-sample traversal, native packet allocation or submission, decoder
execution, production MP4 demux composition, fragmented MP4, streaming,
rendering, and physical execution are not included.
