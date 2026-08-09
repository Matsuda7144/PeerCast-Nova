# ADR-0475: Bounded ISO BMFF Regular Movie Demux Composition

## Status

Accepted as the regular MP4 movie-level demux ownership slice following
ADR-0474.

## Decision

Compose a caller-bounded list of exact regular-track requests over one common
file span into owned AVC and AAC track packet sequences. Require every composed
track to retain the same movie-timing source, unique track identity, and stable
caller order. Then apply one caller-explicit video/audio stream selection and
project its initial cursor set and bounded cross-track merge traversal.

Allow 16 tracks by default, retain the existing 256-track hard limit, and allow
4,096 merge steps by default within the existing traversal hard limit. Require
the caller to select the video and/or audio stream indexes and the equal-time
tie-break policy. Retain the exact track, sequence-set, selection, cursor, and
traversal projections. Reject the first failed stage without returning partial
movie composition.

## Consequences

The caller can now convert already parsed regular MP4 video/audio sources and
their shared file bytes into an immutable same-movie selected packet traversal
in one finite managed operation. Compressed payloads remain owned after the
caller buffer changes, and an exhausted versus truncated merge is explicit.

## Non-goals

Top-level file or `moov` discovery, automatic stream selection, decoder
preparation, runtime capability lookup, native parameter transfer, decoder
open, packet submission, scheduling, delivery, rendering, application
integration, fragmented MP4 production demux, and physical execution are not
included.
