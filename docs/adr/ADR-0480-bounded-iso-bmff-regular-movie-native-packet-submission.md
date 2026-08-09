# ADR-0480: Bounded ISO BMFF Regular Movie Native Packet Submission

## Status

Accepted as the compressed-packet submission boundary following ADR-0479.

## Decision

Consume one exact ADR-0479 regular-movie native decoder session and submit its
precomputed merge traversal one packet at a time. Require every step to retain
its exact merge index, selected stream, role, track, sample, and source packet.
Map the owned packet payload, media-unit presentation timestamp, reconstructed
decode timestamp, duration, selected stream index, `1 / mdhd.timescale` packet
time base, and an IDR-derived video key flag into the existing reviewed
metadata-aware native packet owner.

Retain the same native packet and merge index when `avcodec_send_packet`
reports backpressure. Advance the merge cursor only after that exact packet is
accepted. Treat any other creation or send rejection as terminal for the
submission session and dispose the pending packet plus the complete decoder
session. Distinguish an exhausted source traversal from a caller-bounded
truncated traversal. Keep packet creation and sending injectable so managed
tests never invoke native code.

## Consequences

The regular MP4 production chain now reaches source-exact, timestamp-aware
compressed packet submission with deterministic merge order, retry identity,
and fail-closed ownership. The native adapter reuses the reviewed AVPacket
allocation, padding, metadata-write, and send implementation.

## Non-goals

End-of-stream flush, decoder receive, frame copying, repeated drain progress,
scheduling, delivery, rendering, application integration, fragmented MP4,
runtime discovery or loading, and physical execution are not included.
