# PCN-0429: Bounded ISO BMFF Track Fragment Decode Time

## Decision

Accept strict zero-flag `tfdt` parsing with exact version-zero 32-bit and
version-one 64-bit unsigned decode times, without timeline inference, fragment
traversal, streaming, decoder access, or physical execution.
