# PCN-0343: Bounded ISO BMFF Movie Track Codec Packet Sequence Set

## Decision

Accept immutable caller-ordered ownership of at most 256 exact same-movie track
sequences with unique track IDs and deterministic stream indexes. No default
selection, cross-track merge, native operation, decoder execution, rendering,
or physical execution occurs.
