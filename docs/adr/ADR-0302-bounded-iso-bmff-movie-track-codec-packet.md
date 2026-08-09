# ADR-0302: Bounded ISO BMFF Movie-Timescale-Bound Track Codec Packet

## Status

Accepted as a post-0.0.10e non-physical managed-demux metadata slice.

## Context

ADR-0301 binds an exact codec packet to its track identity, media timing, and
handler metadata. Its edit appearances are expressed as exact rational movie
timeline positions, but the packet does not retain the `mvhd` timing object
that defines that movie timeline.

## Decision

Bind one ADR-0301 track codec packet to the exact parsed `mvhd` timing object.
Retain both exact sources and expose the track ID, media timescale, movie
timescale, and the unchanged complete-sample edit appearances.

Reject missing inputs, a zero movie timescale, no appearance, an invalid or
incomplete appearance, an appearance denominator that differs from the bound
`mdhd` timescale, or an appearance beyond a known movie duration. Preserve
unknown movie duration without inventing a bound. Compare exact rational values
without rounding or converting them to wall-clock time.

## Consequences

A track-bound codec packet now carries the authoritative movie timing source
needed to interpret its exact edit appearances, while known-duration and media
timescale inconsistencies fail closed.

## Non-goals

Stream indexing, track selection policy, demux-event projection or scheduling,
timestamp normalization, multi-sample traversal, native packet submission,
decoder execution, production MP4 demux composition, fragmented MP4, streaming,
rendering, and physical execution are not included.
