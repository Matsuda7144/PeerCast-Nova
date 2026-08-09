# PCN-0312: Bounded ISO BMFF Movie-Timescale-Bound Track Codec Packet

## Decision

Accept exact source-bound association of one track codec packet with parsed
movie timing. Missing or invalid appearances, media-timescale mismatch, and
known movie-duration overflow fail closed. No timestamp rounding, scheduling,
native operation, rendering, or physical execution occurs.
