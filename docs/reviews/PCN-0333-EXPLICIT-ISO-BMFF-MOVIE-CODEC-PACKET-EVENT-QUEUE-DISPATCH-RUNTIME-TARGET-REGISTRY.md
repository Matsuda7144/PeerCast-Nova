# PCN-0333: Explicit ISO BMFF Movie Codec Packet Event Queue Dispatch Runtime Target Registry

## Decision

Accept at most two immutable runtime target registrations containing opaque
target and runtime-instance IDs, video/audio kind, and nonzero generation.
Duplicate and invalid declarations fail closed. No instance reference,
invocation, native operation, rendering, or physical execution is introduced.
