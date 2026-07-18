# Roadmap

## 0.0.1 Heartbeat / Architecture Review

- Foundation and Core lifecycle
- Control plane / data plane separation
- Protocol provenance policy

## 0.0.2a Transport — Implemented

- Cancellable TCP transport
- Finite timeouts, backlog, and connection limits
- Loopback integration tests

## 0.0.2b PCP Framing — Implemented

- Segmented PCP atom parsing
- Parent and leaf framing
- EOF and resource-limit handling
- Synthetic fixtures

## 0.0.2c Compatibility Probe — Verified

- NUL-padded atom names
- HTTP relay entry request
- 200 and 503 response handling
- HELO / OLEH
- OK / QUIT
- Owned control-atom tree codec
- Controlled loopback peer tests
- CLI live probe
- First live IPv4 loopback probe accepted by PeerCastStation 6.0.0.0
- Official Stable 4.1.0 accepted with ordered Atom trace `oleh`, `ok`
- Official master development 7.1.1.0 accepted with the same Atom trace

Completed verification gate:

- stable PeerCastStation live test
- current development PeerCastStation live test
- sanitized interoperability report

## 0.0.3 Channel — Verified

- Channel identity and metadata foundation — Implemented in 0.0.3a
- Session state machine — Implemented in 0.0.3a
- Immutable observable status snapshots — Implemented in 0.0.3a
- Content-free bounded CHAN structure observation — Implemented in 0.0.3b
- First bounded CHAN control parsing — Implemented in 0.0.3b
- Network-to-Core metadata/track mapping — Implemented in 0.0.3c
- 32-bit packet-position unwrap and explicit backward discontinuity —
  Implemented in 0.0.3c
- Post-handshake, one-packet-at-a-time receive boundary — Implemented in 0.0.3d
- Finite owned packet buffer, awaited backpressure, cancellation, QUIT, and
  discontinuity delivery — Implemented in 0.0.3d
- End-to-end TCP/HTTP/PCP session ownership and finite reconnect wiring —
  Implemented in 0.0.3e
- Same-PipeReader handshake handoff, stable reconnect identity/position/sequence,
  idle timeout, cancellation, and privacy-safe finite diagnostics — Implemented
  in 0.0.3e
- Controlled live packet reception on PeerCastStation 6.0.0.0 — Verified in
  0.0.3e
- Fixed privacy-safe fault category, stage, and attempt counts — Implemented in
  0.0.3f
- Official Stable 4.1.0 and development 7.1.1 finite live packet sessions —
  Verified in 0.0.3f
- Per-consumer relay/player/recorder fan-out policies — Deferred to 0.0.4+

Completed verification gate:

- deterministic fault classification without free-form or external values
- official stable PeerCastStation finite live packet session
- current development PeerCastStation finite live packet session
- sanitized two-build Channel Session report

## 0.0.4 Relay — Verified

- Shared owned-payload fan-out — Implemented in 0.0.4a
- Per-child packet and byte bounds — Implemented in 0.0.4a
- Slow-child and failing-sink isolation — Implemented in 0.0.4a
- Finite child shutdown and fixed-field counters — Implemented in 0.0.4a
- Explicit discontinuity delivery to each child — Implemented in 0.0.4a
- Downstream HTTP/PCP child handshake — Implemented in 0.0.4b
- Bounded zero-second-payload-copy CHAN serialization — Implemented in 0.0.4b
- Finite write/handshake/QUIT/cleanup behavior — Implemented in 0.0.4b
- Official Stable 4.1.0 and development 7.1.1 downstream wire interoperability
  — Verified in 0.0.4b
- Long-running bounded listener/accept coordinator — Implemented in 0.0.4c
- Multi-child lifecycle, fixed aggregate status, and finite server stop —
  Implemented in 0.0.4c
- Official Stable 4.1.0 and development 7.1.1 relay-server interoperability —
  Verified in 0.0.4c
- Owned upstream/fan-out/downstream relay coordinator — Implemented in 0.0.4d
- Finite natural-end drain and prompt requested shutdown — Implemented in
  0.0.4d
- Reconnect continuity through one unchanged downstream child — Verified in
  0.0.4d
- Synthetic upstream wire through Nova to official Stable/development
  downstream peers — Verified in 0.0.4d
- Fault-resilient coordinator ingress packet/byte accounting — Implemented and
  verified in 0.0.4e
- Loopback-only finite live relay diagnostic and fixed acceptance gate —
  Implemented and verified with an isolated three-stage chain in 0.0.4f
- Single-command relay/downstream orchestration with conjunctive process gate,
  occupied-port preflight, and owned-job cleanup — Implemented and verified on
  isolated success and failure paths in 0.0.4g
- Coordinator-owned fixed readiness outcome and CLI output-stream
  orchestration without OS TCP-table inspection — Implemented and verified in
  0.0.4h
- Directional legacy HTTP response `obs-text` boundary with strict downstream
  requests — Implemented and verified in 0.0.4 final
- Continuous controlled real-media channel from official PeerCastStation
  6.0.0.0 through Nova to a downstream session — Verified in 0.0.4 final
- Finite 60-second gate with 1,338 ingress packets, four delivered packets,
  zero discard/cleanup/internal failures, and complete shutdown — Verified

Completed verification gate:

- official-upstream HTTP/PCP acceptance
- continuous real-media packet ingress through bounded fan-out
- downstream delivery through Nova's PCP server
- privacy-safe fixed counters and finite complete cleanup

## 0.0.5 Playback — In Progress

- Separate Playback project and one-way dependency from Playback to Network —
  Implemented in 0.0.5a
- Bounded player input with one owned payload copy — Implemented in 0.0.5a
- Latest-wins packet/byte queue and explicit latency discontinuity —
  Implemented in 0.0.5a
- Graceful drain, immediate stop, and outstanding-lease accounting —
  Implemented in 0.0.5a
- Bounded media-container signature observation — Implemented and controlled
  official-upstream verified in 0.0.5b
- Replaceable decoder lifecycle, bounded bootstrap replay, discontinuity reset,
  and finite teardown — Verified in 0.0.5c
- Controlled real-media decoder-lifecycle probe with matching runner/backend
  counters and complete lease cleanup — Verified in 0.0.5d
- Canonical decoded output contract and callback-scoped frame ownership — Verified in 0.0.5e
- Bounded decoded audio/video queues and renderer/device sinks — Next
- Audio/video playback and A/V synchronization — Pending
- Controlled audible/visible real-media playback verification — Pending

The 0.0.5a bounds limit memory and queue depth. The 0.0.5b classifier adds
bounded container evidence without retaining or reporting media prefixes.
The 0.0.5c lifecycle adds backend-neutral bootstrap handoff, discontinuity
reset, and finite decoder teardown without selecting a real decoder. The
0.0.5d probe verifies that controlled real-media ingress traverses that owned
lifecycle and releases all packet leases. A wall-clock latency target still
requires decoded timing evidence and is not yet claimed. The verified 0.0.5e
adds canonical PCM/BGRA32 output metadata and awaited callback ownership without
selecting a codec backend or output device.

## 0.0.6 External Decoder Integration — In Progress

- Bounded external artifact SHA-256 verification — Verified in 0.0.6a
- Exact BtbN FFmpeg 8.1 LGPL shared candidate — Technically verified in 0.0.6b; activation pending
- Installed runtime 5-DLL inventory — Verified in 0.0.6c
- Explicit local fixed-drive locator — Verified in 0.0.6c1
- Verified five-handle runtime lease and fail-closed enum boundary — Verified in 0.0.6c2
- Cross-platform test discovery and artifact consistency — 0.0.6c2-fix1
- FFmpeg ABI loading and symbol gate using the same verified lease — Implemented in 0.0.6d; activation pending
- Native decoder lookup and context ownership lease — Implemented in 0.0.6e; codec open pending
- Managed H.264/AAC codec parameter ownership and padded extradata boundary — Implemented in 0.0.6f; native transfer pending
- Native parameter transfer into the owned decoder context — Implemented in 0.0.6g; codec open pending
- Fixed `avcodec_open2` transition for transferred owned contexts — Implemented in 0.0.6h; packet/frame processing pending
- Bounded encoded-packet send and decoded-frame state receive boundary — Implemented in 0.0.6i; frame extraction pending
- Callback-scoped native frame ownership and unconditional release — Implemented in 0.0.6j
- FFmpeg 8.1 native frame metadata extraction with explicit unknown-time values — Implemented in 0.0.6k
- Callback-scoped validated video plane/audio sample views — Implemented in 0.0.6l
- Owned bounded managed plane/sample copies without conversion — Implemented in 0.0.6m
- Revalidated adapter to existing decoded output models — Implemented in 0.0.6n; YUV/planar conversion pending
- Managed BT.601 YUV and planar PCM conversion to decoded output models — Implemented in 0.0.6o
- Serial awaited delivery to the decoded frame sink without a decoder-owned queue — Implemented in 0.0.6p
- Bounded playback policy and monotonic A/V clock responsibility boundary — Implemented in 0.0.6q; scheduling pending
- Explicitly driven fixed-capacity playback scheduler — Implemented in 0.0.6r
- Separate awaited renderer/audio sink contracts — Implemented in 0.0.6s; OS adapters pending
- Windows 11 D3D11 and WASAPI shared/event-driven selection/probe — Implemented in 0.0.6t; activation pending
- Finite D3D11 and WASAPI native activation ownership boundary — Implemented in 0.0.6u; rendering and stream initialization pending
- Borrowed D3D11/WASAPI resource binding state and generation boundary — Implemented in 0.0.6v; rendering and stream initialization pending
- Finite video target and WASAPI shared/event-driven stream initialization boundary — Implemented in 0.0.6w; rendering, Present, and audio Start pending
- Finite one-frame BGRA32 upload/Present and WASAPI buffer-write/one-time-Start boundary — Implemented in 0.0.6x; scheduling, A/V synchronization, and continuous playback pending
- FLV demux/decode backend — Pending
- Bounded decoded audio/video queues and renderer/device sinks — Pending
- Audio/video playback and A/V synchronization — Pending
- Controlled audible/visible real-media playback verification — Pending

## 0.1.0 Genesis

- Relay and streaming established
- Initial public release
