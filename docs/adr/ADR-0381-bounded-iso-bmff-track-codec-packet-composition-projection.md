# ADR-0381: Bounded ISO BMFF Track Codec-Packet Composition Projection

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Compose one codec packet for every exact ADR-0380 entry in source order. For
`avc1` / `avc3`, require caller-explicit payload requirements and apply the
existing bounded framing, NAL classification, payload composition, and packet
projection chain. For `mp4a`, reject AVC requirements and project the exact
original owned sample bytes with the exact parsed audio entry. Retain the exact
configuration source and return no partial projection.

## Non-goals

AAC decoding or runtime preparation, track/movie binding, sequence selection,
delivery, scheduling, native execution, rendering, or physical execution are
not included.
