# PCN-0315: Bounded ISO BMFF Movie Codec Packet Event Projection

## Decision

Accept separate source-ordered video and audio event lists that retain exact
selected streams, codec packets, and rational edit appearances. Source mismatch,
invalid appearances, and the upstream appearance limit fail closed. No
cross-stream sorting, scheduling, native operation, rendering, or physical
execution occurs.
