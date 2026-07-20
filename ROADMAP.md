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
- 0.0.9h PCP Chunk-to-FLV Parser Composition Review
- 0.0.9i Finite Awaited PCP-to-FLV Composition Boundary

0.0.9i implements the reviewed awaited composition from callback-scoped PCP packet memory through the one-event FLV parser and exact source-bound submission state machine. It preserves callback memory lifetime, exact suffix accounting, bounded parser carry, packet／flush backpressure, explicit finalization, and discontinuity generation replacement without a second encoded queue or production activation.

### Next: 0.0.9j Bounded Decode Iteration and Per-stream EOS Drain Review

Review the finite connection from accepted packet／flush transitions to decoded-frame receive and independent stream drain. The review must define:

- whether frame receive belongs inside the callback operation or a separately driven finite operation;
- one-receive ownership and how a received native frame is converted into the existing callback-scoped metadata／plane boundary;
- packet acceptance followed by zero, one, or multiple bounded decoded frames;
- independent video and audio drain state after accepted null-packet flush;
- decoder EOF versus parser EOS and when each stream is complete;
- per-operation receive count, owned-byte, and no-progress limits;
- cancellation, native rejection, source disposal, discontinuity reset, and terminal cleanup;
- frame ownership transfer without retaining native pointers or adding a second decoded queue;
- composition with the 0.0.9i callback and generation lifetime without hidden resident loops.

The review should remain documentation-only. Runtime decode iteration and frame delivery must be separately implemented and validated afterward.

### Later sequence

1. implement bounded decode iteration and per-stream EOS drain;
2. add decoded-frame ownership and delivery for the real FLV path;
3. add a production generation factory and review backend activation／Windows lifecycle;
4. perform controlled real-media validation.

### Deferred

- MP4／fragmented MP4 parser
- avformat integration
- production generation factory and backend activation
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
