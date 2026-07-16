# ADR-0037: Bounded Latest-Wins Playback Input

- Status: Accepted
- Date: 2026-07-14

## Context

The verified relay path exposes each upstream PCP packet only for the duration
of an awaited callback. A player cannot retain that memory, and it must not
block the upstream session indefinitely when decoding or rendering is slower
than ingress.

Relay, playback, and recording require different loss policies. Disconnecting
a slow playback consumer is unnecessarily fragile, while silently dropping
recorder bytes would be incorrect. Playback instead needs an explicit,
latency-oriented skip policy.

## Decision

Create `Nova.Playback` as a separate project that depends on `Nova.Network`.
`Nova.Network` does not reference Playback, a decoder, a renderer, or UI code.

The first PCP adapter is `PcpPlaybackLatencyBuffer`:

- it implements the existing callback-scoped `IPcpChannelPacketConsumer`;
- it copies an accepted payload once into owned pooled memory;
- both queued packet count and queued payload bytes have hard limits;
- overflow evicts the oldest queued packets until the newest packet fits;
- an individually oversized packet is rejected before a payload copy;
- every source gap or latency skip reaches the reader as a fixed
  `PlaybackDiscontinuityKind` flag;
- only one reader may own a `PlaybackPacketLease` at a time;
- the lease makes payload lifetime explicit and exposes outstanding ownership
  in a content-free status snapshot;
- graceful completion drains queued packets, while owner stop immediately
  discards queued packets and wakes a waiting reader.

An in-flight lease is never revoked. Stop remains finite because the buffer
owns no decoder worker; a caller-held lease remains observable until the
caller disposes it.

## Media-format boundary

The PCP HTTP `Content-Type` describes the PCP transport response and is not a
media-container declaration. Channel metadata such as content type, stream
type, and extension is an untrusted hint, not sufficient proof of a decoder.

Format detection and decoder selection will be a later Playback component. It
must use bounded probing, reset on every discontinuity, and keep payload bytes
out of Core and default diagnostics.

## Consequences

- Playback can remain responsive without coupling decoder speed to upstream
  relay reception.
- Dropped media is explicit and deterministic rather than hidden.
- Packet/byte limits bound memory and queue depth. They do not yet promise a
  wall-clock latency duration because container timestamps are not decoded.
- Recorder input must use a different no-silent-loss policy.
- Audio/video decoding and rendering are not completed by this decision.
