# PCN-0317: Exact ISO BMFF Movie Codec Packet Event Time

## Decision

Accept exact rational seconds metadata for the ADR-0306 ordered events. The
movie-time numerator remains exact and the denominator is multiplied by the
retained nonzero `mvhd` timescale in `ulong`. Exact source identity and order
are retained. No tick conversion, queueing, scheduling, native operation,
rendering, or physical execution occurs.
