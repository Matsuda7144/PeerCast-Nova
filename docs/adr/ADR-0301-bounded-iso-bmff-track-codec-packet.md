# ADR-0301: Bounded ISO BMFF Track Codec Packet

## Status

Accepted as a post-0.0.10e non-physical managed-demux metadata slice.

## Context

ADR-0300 exposes exact supported AVC/AAC packet bytes with edited timing and
sample-description identity, but intentionally omits track identity and the
track's media metadata.

## Decision

Bind one ADR-0300 codec packet to the exact parsed `tkhd`, `mdhd`, and `hdlr`
objects for its track. Retain all four exact sources and project the nonzero
track ID, media timescale, and Video/Audio media kind.

Reject missing inputs, a zero track ID, a packet media timescale that differs
from `mdhd`, or a packet media kind that differs from `hdlr`. Do not normalize,
infer, or silently replace any mismatched value.

## Consequences

A managed packet can now retain its exact track identity and authoritative
media metadata without losing its codec-specific provenance or edited timing.

## Non-goals

Movie timescale binding, stream indexing, track selection policy, demux-event
projection or scheduling, multi-sample traversal, native packet submission,
decoder execution, production MP4 demux composition, fragmented MP4, streaming,
rendering, and physical execution are not included.
