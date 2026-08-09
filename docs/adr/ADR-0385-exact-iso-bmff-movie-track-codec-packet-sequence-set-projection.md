# ADR-0385: Exact ISO BMFF Movie-Track Codec-Packet Sequence-Set Projection

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Own a bounded caller-ordered list of exact ADR-0384 sequence projections and
apply the existing same-movie, unique-track sequence-set factory. Preserve each
exact source projection and its assigned zero-based stream index without
partial results.

## Non-goals

Automatic stream discovery or ranking, stream selection, cursor creation,
merge traversal, delivery, runtime access, or physical execution are not
included.
