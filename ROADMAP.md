# Roadmap

## 0.0.1 Heartbeat / Architecture Review — Implemented

- Foundation and Core lifecycle
- Control plane / data plane separation
- Protocol provenance policy
- Apache License 2.0 project foundation

## 0.0.2 Transport and PCP Compatibility — Verified

- cancellable finite TCP transport
- bounded PCP atom framing and owned trees
- HTTP relay entry, HELO/OLEH, OK/QUIT
- official Stable and development PeerCastStation compatibility probes

## 0.0.3 Channel — Verified

- channel identity, metadata, and session state
- bounded CHAN observation and parsing
- one-packet-at-a-time receive ownership
- finite buffer, backpressure, idle timeout, reconnect, and cleanup
- official Stable and development live packet-session verification

## 0.0.4 Relay — Verified

- bounded shared-payload fan-out
- slow/failing child isolation
- downstream HTTP/PCP handshake and server lifecycle
- reconnect continuity and finite drain
- controlled real-media relay verification through Nova

## 0.0.5 Playback Foundation — Verified

- separate `Nova.Playback` project with one-way dependency on Network
- bounded latest-wins encoded input
- media signature classification
- replaceable backend-neutral decoder lifecycle
- diagnostic decoder lifecycle probe
- canonical decoded audio/video contract with callback-scoped payload
- backend descriptor, selection, provenance, audit, and artifact boundaries

## 0.0.6 External Decoder and Output Boundaries — Implemented; activation pending

### Runtime and native decode

- external artifact verification and pinned FFmpeg candidate
- installed runtime inventory, locator, and verified lease
- ABI/symbol gate
- native decoder context, codec parameter, transfer, and open ownership
- bounded packet send and frame receive
- callback-scoped frame, metadata, and plane/sample boundaries
- managed frame copy

### Managed output

- decoded output adapter
- managed YUV/planar PCM conversion
- serial awaited sink delivery
- playback policy and monotonic clock
- explicitly driven bounded scheduler

### Windows output

- renderer/audio sink contracts
- D3D11 and WASAPI selection, activation, binding, initialization, and buffer processing
- scheduler adapter and finite continuous pump

Production FFmpeg activation, runtime bundling, redistribution, and legal approval
remain pending.

## 0.0.7 Timing and Late-item Handling — Implemented

- one-item A/V timing decisions
- bounded cancellable wait/redecision
- late item disposition and coordination
- scheduler reservation disposition
- late-aware scheduler, dispatch, and continuous pump boundaries
- no complete synchronization, resampling, drift correction, or real-media claim

## 0.0.8 Recovery and Diagnostic Integration — In Progress

### Recovery and run control

- 0.0.8a Audio Recovery Boundary — Implemented
- 0.0.8b Recovery-aware Pump Orchestrator — Implemented
- 0.0.8c Deadline-aware Run Budget Policy — Implemented

### Diagnostic gate and fixture

- 0.0.8d Diagnostic Playback Pipeline Integration Probe Gate — Implemented
- 0.0.8e Bounded Diagnostic Playback Fixture — Verified
- 0.0.8f Diagnostic Fixture Adapter — Implemented
- 0.0.8g Diagnostic Decoder Configuration — Implemented
- 0.0.8h Diagnostic Native Parameter Transaction — Implemented
- 0.0.8i Diagnostic Codec Open Transaction — Implemented
- 0.0.8j Diagnostic Packet / Frame Transaction — Implemented

### Diagnostic frame path

- 0.0.8k Diagnostic Frame Metadata Transaction — Implemented
- 0.0.8l Diagnostic Frame Data Transaction — Implemented
- 0.0.8m Diagnostic Managed Frame Copy Transaction — Implemented
- 0.0.8n Diagnostic Managed Format Conversion Transaction — Implemented
- 0.0.8o Diagnostic Awaited Sink Delivery Transaction — Verified

Each transaction is finite and diagnostic-only. Native pointers and converted
callback payload do not survive their ownership boundary. No transaction is
referenced by `PlaybackDecoderBackendFactory` or the production activation path.

### Cutover review

- 0.0.8p Playback Cutover Architecture Review — Accepted

The review fixes the future composition seam at the owned managed frame before
conversion. The existing scheduler must be reused; no second decoded-frame
queue is approved.

### Next

- 0.0.8q Bounded Scheduler Aggregate Byte Budget

Add a configurable aggregate owned-byte budget beside frame-count capacity.
The scheduler must account plane/sample bytes with checked arithmetic, reject
single-frame oversize input, apply full policies against both bounds, protect
reservations, expose current byte usage, and release accounting on every drop,
completion, reset, and disposal path.

Only after 0.0.8q is verified may a later milestone connect diagnostic managed
frames to scheduler ownership.

### Deferred within 0.0.8+

- bounded FLV/MP4 demux contract
- decoded ingress sequence/discontinuity policy
- duration-based decoded-buffer cutoff after reliable timing evidence exists
- complete start/recover/stop diagnostic execution
- controlled audible/visible playback verification

## Platform, dependency, and asset policy

- Target framework remains `net10.0`; CI and release builds use supported servicing updates.
- Production code adds no dependency solely to implement scheduler budgeting.
- FFmpeg remains host-installed and dynamically loaded while approval is pending.
- Third-party media, logo, icon, font, and binary assets require recorded provenance, license, and redistribution permission before inclusion.
- A `Nova.Playback.Windows` project split is deferred until a concrete non-Windows host or packaging dependency requires it.

## 0.1.0 Genesis

Release is gated on:

- verified Relay and streaming foundation
- bounded demux and production decoder composition
- approved runtime/distribution decision
- finite decoded buffering and output lifecycle
- controlled audible/visible real-media verification
- user-facing application surface, packaging, and documentation
