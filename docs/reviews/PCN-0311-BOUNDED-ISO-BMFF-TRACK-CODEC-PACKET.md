# PCN-0311: Bounded ISO BMFF Track Codec Packet

## Decision

Accept exact source-bound association of one codec packet with parsed track
identity, media timing, and handler metadata. Track ID, timescale, and media-kind
mismatches fail closed. No scheduling, native operation, rendering, or physical
execution occurs.
