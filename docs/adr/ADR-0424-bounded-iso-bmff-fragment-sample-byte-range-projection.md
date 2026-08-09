# ADR-0424: Bounded ISO BMFF Fragment Sample Byte-Range Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Project one resolved run's sample sizes from one resolved nonnegative run data
start into contiguous immutable byte-range metadata. Preserve zero-size samples
without advancing the cursor and use arbitrary-precision offsets and exclusive
ends so cumulative positions are not narrowed.

## Non-goals

File-length validation, `mdat` discovery or containment, payload views or
ownership, fragment traversal, streaming, decoder submission, native execution,
rendering, or physical execution are not included.
