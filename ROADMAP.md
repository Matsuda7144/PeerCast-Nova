# Roadmap

## 0.0.1-0.0.4 Network Foundation — Verified

- Foundation／Core lifecycle
- bounded TCP／PCP／Channel
- official PeerCastStation compatibility
- Relay fan-out and controlled real-media relay verification

## 0.0.5 Playback Foundation — Verified

- separate `Nova.Playback` project
- bounded encoded input
- replaceable decoder lifecycle
- canonical decoded output contract
- backend provenance and audit boundaries

## 0.0.6 Decoder and Output Boundaries — Implemented; activation pending

- verified FFmpeg runtime lease and ABI gate
- native context through packet／frame processing
- callback-scoped frame data
- managed copy、conversion、awaited delivery
- bounded scheduler and Windows output boundaries

## 0.0.7 Timing and Late-aware Dispatch — Implemented

- monotonic timing
- late-item policy
- reservation handling
- finite pumping

## 0.0.8 Recovery and Diagnostic Integration — In Progress

### Verified sequence

- 0.0.8a-0.0.8c Recovery and Run Budget
- 0.0.8d-0.0.8j Diagnostic Gate through Packet／Frame
- 0.0.8k Frame Metadata
- 0.0.8l Frame Data
- 0.0.8m Managed Frame Copy
- 0.0.8n Managed Format Conversion
- 0.0.8o Awaited Sink Delivery
- 0.0.8p Playback Cutover Review
- 0.0.8q Scheduler Aggregate Byte Budget
- 0.0.8r Managed Frame Scheduler Ingress
- 0.0.8s Scheduler Dispatch Architecture Review
- 0.0.8t Diagnostic Late-aware Scheduler Dispatch
- 0.0.8u Bounded Demux Output Contract Review

0.0.8u confirms that production composition must not depend on the fixed diagnostic
fixture. The next input-side foundation is a container-neutral, pure managed,
bounded demux output contract carrying stream descriptions, owned packet payloads,
separate PTS and DTS, keyframe and discontinuity state, and explicit EOS.

### Next

- 0.0.8v Pure Managed Bounded Demux Output Contract

Implement immutable H.264／AAC stream descriptions and finite packet／EOS
completions with caller-driven backpressure, checked count and byte limits,
idempotent ownership cleanup, and fixed fail-closed results. Do not parse FLV or
MP4 and do not change an activation path.

### Later sequence

1. one bounded container parser behind the shared contract;
2. codec-parameter and packet-submission composition review;
3. bounded decode iteration, drain, and EOS handling;
4. explicit production activation and Windows lifecycle review;
5. controlled real-media validation.

### Deferred

- bounded FLV／MP4 parser
- avformat integration
- production decoder ingress
- continuous decode、flush、EOS drain
- duration cutoff
- complete synchronization
- controlled audible／visible playback

## CI policy

Routine GitHub Actions use Ubuntu only. Superseded runs on the same PR are
cancelled. Windows CI is reserved for explicit milestone or release validation.

## Platform and dependency policy

- retain `net10.0`
- avoid speculative production dependencies
- keep FFmpeg host-installed and dynamically loaded
- record provenance and redistribution permission for third-party assets

## 0.1.0 Genesis gate

- bounded demux and production composition
- approved runtime／distribution decision
- finite decoded buffering and output lifecycle
- controlled audible／visible verification
- user-facing application and packaging
