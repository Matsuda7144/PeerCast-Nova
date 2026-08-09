# ADR-0303: Bounded ISO BMFF Movie Codec Packet Index

## Status

Accepted as a post-0.0.10e non-physical managed-demux metadata slice.

## Context

ADR-0302 binds one current codec packet to its exact track and movie timing
sources. A later demux boundary needs a stable stream index, but no track
selection or multi-sample traversal policy has been accepted.

## Decision

Snapshot one caller-ordered set of one through 256 ADR-0302 movie track codec
packets. Assign contiguous zero-based stream indexes in exactly that order and
retain every exact packet plus the shared exact `mvhd` timing source.

Reject a missing or empty set, more than 256 packets, any missing packet, any
packet bound to a different `mvhd` object even when its values match, or a
duplicate track ID. Copy the ordered references into a read-only snapshot so a
later caller mutation cannot change the accepted index.

The caller-provided order is a fact at this boundary, not an inferred
preference or default-track decision.

## Consequences

One bounded current-packet snapshot can expose stable stream indexes without
losing exact track, codec, edit, or movie-timing provenance.

## Non-goals

Track selection, alternate groups, enabled/default flags, language preference,
demux-event projection or scheduling, multi-sample traversal, native packet
submission, decoder execution, production MP4 demux composition, fragmented
MP4, streaming, rendering, and physical execution are not included.
