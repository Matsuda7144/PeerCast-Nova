# ADR-0305: Bounded ISO BMFF Movie Codec Packet Event Projection

## Status

Accepted as a post-0.0.10e non-physical managed-demux metadata slice.

## Context

ADR-0304 retains exact explicitly selected video and/or audio current packets,
but no event object associates their stream indexes and complete edit
appearances for downstream managed demux composition.

## Decision

Project one immutable event for every complete edit appearance of each selected
current packet. Retain the exact selection, indexed stream, codec packet, and
appearance, and expose its appearance ordinal, stream index, track ID, media
kind, exact rational movie interval, and movie timescale.

Keep video and audio event lists separate and preserve source appearance order
within each list. Do not compare or sort rational times across streams. Represent
an unselected role with an empty read-only list.

Reject a missing selection, a selected stream that is not the exact object at
its retained index, a missing or invalid complete appearance, or more than the
upstream hard limit of 65,536 appearances per stream. The selected two-stream
projection is consequently capped at 131,072 events.

## Consequences

Downstream managed composition can consume bounded source-bound packet event
facts without introducing cross-timescale ordering or a playback clock.

## Non-goals

Cross-stream sorting, timestamp rounding or normalization, demux-event queues,
scheduling, clock selection, multi-sample traversal, native packet submission,
decoder execution, production MP4 demux composition, fragmented MP4, streaming,
rendering, and physical execution are not included.
