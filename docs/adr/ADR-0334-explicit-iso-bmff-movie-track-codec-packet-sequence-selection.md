# ADR-0334: Explicit ISO BMFF Movie Track Codec Packet Sequence Selection

## Status

Accepted as a post-0.0.10e non-physical managed demux-metadata slice.

## Context

ADR-0333 assigns stable stream indexes to bounded same-movie track sequences,
but deliberately does not infer which video or audio sequence should be used.

## Decision

Require the caller to provide an optional video stream index, an optional audio
stream index, or both. Retain the exact ADR-0333 sequence set and exact selected
indexed-sequence objects.

Reject a missing set, a request with neither sequence, a negative or
out-of-range index, one index requested for both media roles, a non-video
sequence requested as video, or a non-audio sequence requested as audio. Permit
explicit video-only and audio-only selection.

Do not scan for a matching sequence, rank tracks, or substitute another
sequence when the request is invalid.

## Consequences

Downstream managed composition can consume explicit, source-bound track
sequences without treating caller order, handler order, or track flags as an
implicit preference.

## Non-goals

Automatic/default track selection, alternate groups, enabled flags, language
preference, user preference persistence, cursor creation, packet-head
comparison, cross-track merge, event projection, scheduling, automatic packet
construction, file or `mdat` reads, native packet submission, decoder execution,
rendering, or physical execution are included.
