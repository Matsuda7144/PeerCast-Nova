# ADR-0465: Exact ISO BMFF Fragment Sample Merge Traversal Submission Manifest Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Flatten each selected submission intent into immutable manifest metadata while
retaining its exact selection candidate. Expose contiguous manifest and source
indexes, opaque consumer and batch identifiers, track, fragment batch,
sequence, source sample, media-timescale, sample-size, exact byte-range, and
deadline identity. Revalidate strictly increasing source order, included-source
identity, nonempty identifiers, and size-matched nonnegative byte ranges.

## Non-goals

Payload bytes or ownership, authorization, scheduling, runtime lookup,
capability access, invocation, dispatch, delivery, native execution, rendering,
or physical execution are not included.
