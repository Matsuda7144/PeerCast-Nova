# PCN-0329: Exact ISO BMFF Movie Codec Packet Event Queue Dispatch Target Binding

## Decision

Accept immutable media-kind binding from each exact ADR-0317 candidate to its
opaque ADR-0318 video or audio target identity. Fail closed on source, presence,
or media-kind mismatch. No target resolution, invocation, dispatch, cursor
mutation, native operation, rendering, or physical execution occurs.
