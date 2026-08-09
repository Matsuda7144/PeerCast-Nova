# ADR-0474: Bounded ISO BMFF Regular Track Demux Composition

## Status

Accepted as the first post-ADR-0473 production-oriented regular MP4 managed
composition slice.

## Decision

Compose one caller-bounded regular non-fragmented MP4 track from existing exact
parsed sources and a caller-provided file span into one owned
movie-timescale-bound AVC or AAC packet sequence. Reuse the established sample
plan, edit admission, byte-range, `mdat` containment, payload ownership,
sample-description, codec-configuration, packet-composition, and track/movie
binding boundaries without reimplementing them.

Use one caller-selected first sample and a default maximum of 4,096 samples,
with the existing 131,072-sample hard limit. Apply the same bound to admission
selection so the composition cannot silently create a larger second work set.
Copy every admitted compressed payload before returning, retain exact parsed
source identity through the final sequence, require caller-explicit AVC payload
requirements, and reject at the first failed stage without a partial result.

## Consequences

The caller can now turn an already parsed regular MP4 track and its file bytes
into an immutable owned compressed-packet sequence with exact edit timing,
track identity, movie timing, media kind, codec configuration, and truncation
state in one finite managed operation. This is the first cohesive connection of
the existing regular MP4 foundation toward production demux ownership.

## Non-goals

Top-level file or `moov` discovery, multi-track composition or selection,
cross-track traversal, decoder-preparation ownership, runtime verification,
native parameter transfer, decoder open, packet submission, scheduling,
delivery, rendering, streaming, fragmented MP4 production demux, and physical
execution are not included.
