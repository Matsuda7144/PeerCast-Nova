# PCN-0331: Explicit ISO BMFF Movie Codec Packet Event Queue Dispatch Target Capability Set

## Decision

Accept at most two immutable target capability declarations containing exact
opaque ID, video/audio media kind, and positive maximum payload size. Duplicate
and invalid declarations fail closed. No invocable target, dispatch, native
operation, rendering, or physical execution is introduced.
