# ADR-0467: Declarative ISO BMFF Fragment Sample Merge Traversal Submission Manifest Summary

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Derive immutable aggregate facts from one exact manifest-source projection.
For a populated manifest retain its common consumer, batch, and track identity,
total sample bytes, minimum and maximum byte-range envelope, and earliest/latest
decode and presentation deadline bounds. An empty manifest reports zero bytes
and no synthetic identity, range, or deadline. Revalidate every exact manifest
entry, common identity, size-matched range, and nonempty deadline interval.

## Non-goals

Payload access, authorization, scheduling, clock reads, runtime lookup,
capability access, invocation, dispatch, delivery or completion claims, native
execution, rendering, or physical execution are not included.
