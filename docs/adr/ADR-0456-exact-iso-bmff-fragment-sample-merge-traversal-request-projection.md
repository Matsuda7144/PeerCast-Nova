# ADR-0456: Exact ISO BMFF Fragment Sample Merge Traversal Request Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Project each selected fragment candidate into immutable target-free request
metadata. Retain zero-based request index, strictly increasing selection source
index, nonzero track ID, fragment batch index and sequence number, source sample
index, media timescale, sample size and flags, exact byte range, and deadline
identity. Validate exact source-order candidate identity and require byte-range
length to equal sample size without reading payload bytes.

## Non-goals

Payload access or ownership, sample-description or codec resolution, target
selection, capability binding, scheduling, dispatch, delivery attempt or
outcome, decoder submission, native execution, rendering, or physical execution
are not included.
