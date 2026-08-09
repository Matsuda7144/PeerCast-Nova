# ADR-0421: Bounded ISO BMFF Fragment Sample Default Resolution

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Resolve one already parsed `trun` against matching `tfhd` and `trex` metadata.
Require equal nonzero track identities, a nonzero resolved sample-description
index, and exact sample-count consistency. Resolve duration and size in
per-sample, track-fragment, then track-extends order. Resolve flags from
per-sample flags, first-sample flags for index zero, track-fragment defaults,
then track-extends defaults. Preserve explicitly present zero values and
composition offsets, and return an immutable sample projection.

## Non-goals

Fragment/container traversal, `tfdt` application, decode/presentation timestamp
accumulation, absolute data-offset calculation, byte-range or media-data lookup,
streaming, decoder submission, native execution, rendering, or physical
execution are not included.
