# ADR-0040: Decoder Lifecycle Probe

- Status: Accepted
- Date: 2026-07-16

## Context

Milestone 0.0.5c defines and deterministically tests a backend-neutral decoder
lifecycle, but those tests use synthetic packets. Before selecting a real codec
backend, the project needs controlled evidence that official PeerCastStation
input can pass through classification, bootstrap replay, sequential decoder
callbacks, finite owner stop, decoder disposal, and packet-lease cleanup.

Calling this evidence a decode or playback test would be misleading because no
codec, decoded sample, renderer, clock, or audible/visible output is involved.

## Decision

Add a loopback-oriented CLI diagnostic named
`--playback-decoder-lifecycle-probe`.

- The probe uses the existing `PcpChannelSession`, bounded
  `PcpPlaybackLatencyBuffer`, and `PlaybackDecoderRunner` without changing
  their ownership contracts.
- A CLI-internal cooperative decoder records only input, byte, reset, complete,
  and dispose counters. It does not inspect, copy, retain, transform, or report
  payload contents.
- The diagnostic requests stop after a configurable minimum number of decoder
  input callbacks. Session stop and runner stop remain separate owned
  operations with finite existing deadlines.
- A wall-clock duration remains an independent terminal boundary even if the
  session or runner completes first.
- The acceptance gate requires recognized container evidence, matching runner
  and decoder counters, exactly one factory creation and decoder disposal, no
  deferred cleanup, a stopped session with HTTP 200, no active-buffer skips, no final pending or
  outstanding packet lease, and complete session/runner shutdown.
- Output contains fixed enums, numbers, and booleans only. Channel ID, endpoint,
  metadata, positions, payload, prefix bytes, exception text, and backend text
  are not returned.
- The command is explicitly a lifecycle probe. It must not claim successful
  media decoding or playback.

## License boundary

The probe decoder is original Apache-2.0 CLI diagnostic code. It contains no
codec implementation, generated binding, native binary, dynamically loaded
library, or copied third-party algorithm. The decoder backend license boundary
accepted in ADR-0039 remains unchanged.

## Consequences

- The 0.0.5c lifecycle can be exercised with controlled real-media ingress
  before a backend is selected.
- Counter agreement detects lost, duplicated, or retained callback input
  without exposing stream contents.
- The probe cannot establish codec compatibility, decoded-frame ownership,
  rendering correctness, A/V synchronization, or audible/visible playback.
- A real decoder remains a later, separately reviewed milestone.
