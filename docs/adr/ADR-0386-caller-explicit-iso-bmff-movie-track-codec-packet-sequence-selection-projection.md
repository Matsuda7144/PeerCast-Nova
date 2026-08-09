# ADR-0386: Caller-Explicit ISO BMFF Movie-Track Codec-Packet Sequence Selection Projection

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply the existing sequence selector to one exact ADR-0385 set using
caller-explicit optional video and audio stream indexes. Retain the request,
exact set projection, selected indexed streams, and exact originating sequence
projections. Reject implicit empty selection, duplicate indexes, bounds errors,
and media-kind mismatches without fallback.

## Non-goals

Default-track inference, ranking, cursor creation, merge traversal, delivery,
runtime access, native execution, rendering, or physical execution are not
included.
