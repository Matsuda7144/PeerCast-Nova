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

0.0.9f connects controlled real FLV bytes through the bounded parser, exact source identity, and finite one-event state machine. It validates packet interleave, timing, keyframe state, retained packet／flush backpressure, one-receive／one-retry bounds, and separately emitted per-stream EOS without native activation.

The validation also confirms that ordinary final input currently aggregates video and audio EOS into one completion. That completion is rejected fail closed by the one-event state machine and must be sequenced before PCP composition.

### Next: 0.0.9g One-event Final EOS Sequencing

Update final FLV parser progress so that:

- one operation emits at most one source-owned per-stream EOS event;
- when both streams remain open, video EOS is emitted first and audio EOS remains pending;
- the next final operation emits the remaining audio EOS and completes finalization;
- an already-ended stream is never emitted again;
- per-stream sequence commits only with successful completion construction;
- active-completion backpressure remains unchanged;
- no detached EOS completion, copied queue, resident loop, or hidden retry is introduced;
- controlled dual-EOS rejection becomes a successful one-event finalization path.

### Later sequence

1. review PCP chunk-to-parser composition and discontinuity ownership;
2. add bounded decode iteration and per-stream EOS drain;
3. add decoded-frame ownership and delivery for the real FLV path;
4. review production activation and Windows lifecycle;
5. perform controlled real-media validation.

### Deferred

- PCP-to-parser composition
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
