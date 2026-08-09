# PCN-0345: Bounded ISO BMFF Selected Track Codec Packet Sequence Cursor Set

## Decision

Accept immutable initial cursors for exactly the caller-selected ADR-0334 video
and/or audio sequence roles. Any selected cursor-creation failure rejects the
whole set without a partial result. No cursor advance, packet-head comparison,
merge, native operation, rendering, or physical execution occurs.
