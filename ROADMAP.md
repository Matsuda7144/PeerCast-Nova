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

0.0.9d connects one exact source-owned FLV packet or per-stream EOS completion to one finite FFmpeg packet send or null flush. It preserves exact metadata, validates source／stream／time-base identity, uses FFmpeg-owned padded storage, retains packet and parser ownership under `EAGAIN`, and introduces no receive loop, hidden retry, or second encoded queue.

### Next: 0.0.9e Finite One-event Packet Submission State Machine

Implement one finite caller-visible state machine that:

- owns exactly one active parser completion at a time;
- begins with one 0.0.9d packet send or null flush attempt;
- performs at most one explicitly bounded frame receive progress step per operation;
- retries a retained packet or pending flush only after receive progress;
- preserves source, stream, media kind, sequence, and per-stream EOS identity;
- returns explicit submitted, output-available, more-input, drain-required, EOF, rejection, and fault transitions;
- consumes parser completion only after accepted native progress;
- adds no resident loop, sleep, spin, hidden retry, second encoded queue, PCP wiring, or activation.

### Later sequence

1. validate the finite state machine with a retained controlled FLV fixture;
2. review PCP chunk-to-parser composition and discontinuity ownership;
3. add bounded decode iteration and per-stream EOS drain;
4. review production activation and Windows lifecycle;
5. perform controlled real-media validation.

### Deferred

- controlled retained FLV fixture validation
- PCP-to-parser composition
- MP4／fragmented MP4 parser
- avformat integration
- production decoder ingress
- continuous decode and per-stream EOS drain
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
