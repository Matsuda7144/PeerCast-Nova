# ADR-0383: Exact ISO BMFF Movie-Track Codec-Packet Binding Projection

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Bind every exact ADR-0382 track packet in source order to one caller-provided
exact `mvhd` timing object. Apply the existing complete-edit-appearance,
media-timescale, and known-movie-duration checks to every entry and retain the
exact track binding and movie timing without partial results.

## Non-goals

Multi-track collection or selection, cross-track merge, delivery, scheduling,
runtime access, native execution, rendering, or physical execution are not
included.
