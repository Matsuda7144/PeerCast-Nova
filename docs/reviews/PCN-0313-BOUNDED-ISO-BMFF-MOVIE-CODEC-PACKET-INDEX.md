# PCN-0313: Bounded ISO BMFF Movie Codec Packet Index

## Decision

Accept a read-only snapshot of at most 256 distinct-track current codec packets
that share the exact same movie-timing object. Caller order becomes contiguous
zero-based stream indexes. No preference inference, selection, scheduling,
native operation, rendering, or physical execution occurs.
