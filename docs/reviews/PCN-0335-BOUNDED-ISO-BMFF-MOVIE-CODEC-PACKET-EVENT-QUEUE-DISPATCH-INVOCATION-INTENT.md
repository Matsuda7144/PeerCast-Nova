# PCN-0335: Bounded ISO BMFF Movie Codec Packet Event Queue Dispatch Invocation Intent

## Decision

Accept bounded immutable invocation-intent projection with one caller-explicit
nonempty invocation ID, exact source-order indexes, exact lookup identity, and
opaque runtime-instance ID and generation. No runtime instance access,
invocation, dispatch, native operation, rendering, or physical execution
occurs.
