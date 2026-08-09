# ADR-0384: Exact ISO BMFF Movie-Track Codec-Packet Sequence Projection

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Project one exact ADR-0383 movie-track packet binding into the existing bounded
same-track immutable packet sequence. Retain the exact binding source and apply
the existing shared movie/track/media/handler identity and strictly increasing
sample-number checks without returning a partial sequence.

## Non-goals

Multi-track collection or selection, cursor creation, merge traversal,
delivery, runtime access, native execution, rendering, or physical execution
are not included.
