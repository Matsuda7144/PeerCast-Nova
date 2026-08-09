# PCN-0427: Bounded ISO BMFF Movie Fragment Header

## Decision

Accept strict FullBox-v0 `mfhd` parsing with an exact opaque sequence number,
without fragment traversal, continuity inference, streaming, decoder access, or
physical execution.
