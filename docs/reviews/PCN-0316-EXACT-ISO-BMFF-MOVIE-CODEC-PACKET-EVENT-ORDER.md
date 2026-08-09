# PCN-0316: Exact ISO BMFF Movie Codec Packet Event Order

## Decision

Accept exact rational cross-stream ordering with deterministic end-time,
stream-index, and appearance-index ties. `BigInteger` comparison prevents
`Int128` cross-product overflow. No timestamp conversion, scheduling, native
operation, rendering, or physical execution occurs.
