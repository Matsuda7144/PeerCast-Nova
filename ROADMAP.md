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
- managed copy、conversion、awaited delivery
- bounded scheduler and Windows output boundaries

## 0.0.7 Timing and Late-aware Dispatch — Implemented

- monotonic timing
- late-item policy
- reservation handling
- finite pumping

## 0.0.8 Recovery, Demux, and Parameter Binding — Verified

- recovery and finite run budgeting
- diagnostic native packet／frame path
- managed frame copy, conversion, scheduler ingress, and late-aware dispatch
- bounded demux output contract
- pure managed bounded FLV v1 parser
- parser-to-decoder composition review
- exact FLV session decoder-parameter binding

## 0.0.9 Source-bound Native Decoder and Packet Ownership — In Progress

### Completed

- 0.0.9a Timestamp-aware Packet Submission Architecture Review
- 0.0.9b Source-bound Opened Native Decoder Session
- 0.0.9c AVPacket Metadata and Flush ABI Review
- 0.0.9d Metadata-aware Native Packet and Flush Boundary
- 0.0.9e Finite One-event Packet Submission State Machine
- 0.0.9f Controlled Retained FLV Fixture Validation
- 0.0.9g One-event Final EOS Sequencing

0.0.9g adds the finite FLV parser facade selected after controlled validation. Ordinary parser completions remain unchanged, while final input emits video then audio EOS through separate source-owned completions. Final signaling, sequence commit, active-completion backpressure, explicit EOS, truncated carry, and discontinuity reset are bounded without detached completions or another encoded queue.

### Next: 0.0.9h PCP Chunk-to-FLV Parser Composition Review

Review the finite connection from the awaited PCP data callback to the one-event FLV parser facade. The review must define:

- callback-scoped chunk-memory ownership and whether copying is ever required;
- consumed-byte accounting and caller-owned suffix retention;
- behavior when one parser or decoder completion remains active;
- how decoder `EAGAIN` pauses further PCP chunk consumption;
- final callback and one-event final EOS signaling;
- cancellation, timeout, source disposal, and callback failure;
- discontinuity generation and source-session replacement;
- cleanup order across parser, native session, retained packet, and callback ownership;
- finite per-callback work with no resident parse／decode loop or second encoded queue.

The review is documentation-only. Runtime composition must remain a later separately validated milestone.

### Later sequence

1. implement the reviewed PCP chunk-to-parser finite composition;
2. add bounded decode iteration and per-stream EOS drain;
3. add decoded-frame ownership and delivery for the real FLV path;
4. review production activation and Windows lifecycle;
5. perform controlled real-media validation.

### Deferred

- PCP-to-parser runtime composition
- MP4／fragmented MP4 parser
- avformat integration
- production decoder ingress
- continuous decode and per-stream EOS drain
- decoded-frame ownership and delivery for the real FLV path
- discontinuity reset and `avcodec_flush_buffers`
- duration cutoff
- complete synchronization
- controlled audible／visible playback

## CI policy

Routine Linux CI uses CircleCI while GitHub Actions usage is exhausted. Final branch heads must pass restore, Release build, all tests, and format verification. Windows CI remains reserved for explicit milestone or release validation.

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
