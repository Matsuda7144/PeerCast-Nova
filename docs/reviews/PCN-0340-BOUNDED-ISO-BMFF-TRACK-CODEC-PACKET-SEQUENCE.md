# PCN-0340: Bounded ISO BMFF Track Codec Packet Sequence

## Decision

Accept immutable ownership of at most 131,072 exact same-source track codec
packets with strictly increasing nonzero sample numbers and deterministic source
indexes. No automatic sample lookup, payload read, cursor traversal, native
operation, decoder execution, rendering, or physical execution occurs.
