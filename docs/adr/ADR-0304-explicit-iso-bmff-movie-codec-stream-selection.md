# ADR-0304: Explicit ISO BMFF Movie Codec Stream Selection

## Status

Accepted as a post-0.0.10e non-physical managed-demux metadata slice.

## Context

ADR-0303 assigns stable stream indexes to one bounded current-packet snapshot,
but deliberately does not infer which video or audio track should be used.

## Decision

Require the caller to provide an optional video stream index, an optional audio
stream index, or both. Retain the exact ADR-0303 index and exact selected stream
objects.

Reject a missing index, a request with neither stream, a negative or out-of-range
index, one index requested for both media roles, a non-video stream requested as
video, or a non-audio stream requested as audio. Permit explicit video-only and
audio-only selection.

Do not scan for a matching stream, rank tracks, or substitute another stream
when the request is invalid.

## Consequences

Downstream managed composition can consume an explicit, source-bound media
selection without treating caller order, handler order, or track flags as an
implicit preference.

## Non-goals

Automatic/default track selection, alternate groups, enabled flags, language
preference, user preference persistence, demux-event projection or scheduling,
multi-sample traversal, native packet submission, decoder execution, production
MP4 demux composition, fragmented MP4, streaming, rendering, and physical
execution are not included.
