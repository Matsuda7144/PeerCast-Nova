# PCN-0330: Bounded ISO BMFF Movie Codec Packet Event Queue Dispatch Request Projection

## Decision

Accept bounded immutable request metadata exposing the exact ADR-0319 binding,
opaque target ID, media kind, codec packet and payload, and deadline. No target
resolution, invocation, dispatch, cursor mutation, native operation, rendering,
or physical execution occurs.
