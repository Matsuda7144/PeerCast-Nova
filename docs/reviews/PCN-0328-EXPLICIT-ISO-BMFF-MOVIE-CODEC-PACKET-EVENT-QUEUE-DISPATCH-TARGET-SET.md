# PCN-0328: Explicit ISO BMFF Movie Codec Packet Event Queue Dispatch Target Set

## Decision

Accept an immutable set of at most one video and one audio opaque nonempty
`Guid` target identity, with duplicates rejected. No delegate, callback,
invocable object, native resource, dispatch, rendering, or physical execution
is owned.
