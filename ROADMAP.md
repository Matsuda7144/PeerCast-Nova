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

0.0.9h selects one awaited, zero-suffix-copy composition from callback-scoped PCP packet memory through the one-event FLV parser and source-bound submission state machine. The callback remains incomplete until its full payload is consumed and no parser／decoder completion remains active. Exact consumed-byte accounting, bounded decoder progress, explicit owner finalization, discontinuity generation replacement, and cleanup ordering are fixed without a second encoded queue or runtime activation.

### Next: 0.0.9i Finite Awaited PCP-to-FLV Composition Boundary

Implement one serialized boundary that:

- implements `IPcpChannelPacketConsumer` without retaining callback packet or payload memory;
- validates exact PCP channel ID, session ID, and monotonic packet sequence;
- consumes one callback payload through a local offset advanced only by parser `ConsumedByteCount`;
- supports multiple FLV records in one callback and one split record in bounded parser carry;
- processes each parser completion before resuming the same callback suffix;
- creates one exact source-bound generation after `StreamDescriptionAvailable` through an injected generation factory;
- pauses parser and new PCP input while a parser or decoder completion remains active;
- bounds packet／flush `EAGAIN` progress through finite state-machine transitions;
- fails closed on no receive progress, contradictory accounting, cancellation, undefined result, or operation-budget exhaustion;
- exposes separate owner-invoked finalization and never infers EOS from transport state;
- replaces the decoder generation in the reviewed order on explicit discontinuity;
- exposes fixed privacy-safe terminal state and idempotent ordered cleanup;
- adds no production backend activation, decoded-frame delivery, output wiring, or second encoded queue.

### Later sequence

1. add bounded decode iteration and per-stream EOS drain;
2. add decoded-frame ownership and delivery for the real FLV path;
3. review production activation and Windows lifecycle;
4. perform controlled real-media validation.

### Deferred

- MP4／fragmented MP4 parser
- avformat integration
- production decoder ingress and backend activation
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
