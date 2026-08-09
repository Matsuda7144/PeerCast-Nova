# PCN-0341: Bounded ISO BMFF Track Codec Packet Sequence Cursor

## Decision

Accept pure immutable traversal of ADR-0330 entries through exact one-step
advance and explicit exhaustion, with sequence/index/sample revalidation. No
source mutation, delivery claim, payload read, native operation, decoder
execution, rendering, or physical execution occurs.
