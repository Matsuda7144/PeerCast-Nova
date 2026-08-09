# ADR-0440: Bounded ISO BMFF Fragment Sample Merge Traversal Acknowledgement View

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Project one immutable acknowledgement metadata item for every exact step in a
completed fragment sample merge traversal. Assign zero-based traversal indexes,
retain each source step, sample, source cursor, and successor cursor, and
revalidate completion facts plus the contiguous cursor chain before projection.
The acknowledgement records observation of exact traversal metadata only.

## Non-goals

Delivery attempt or outcome, cursor mutation, clock acquisition or conversion,
scheduling, discontinuity action, `mdat` or payload access, decoder packet
composition, native execution, rendering, or physical execution are not
included.
