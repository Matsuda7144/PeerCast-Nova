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
- 0.0.9j Bounded Decode Iteration and Per-stream EOS Drain Review
- 0.0.9k Source-bound One-frame Receive and EOS Drain Boundary
- 0.0.9l Awaited Decoded Completion Composition Review
- 0.0.9m Finite Awaited Decoded Completion Composition Boundary

0.0.9m adds one serialized awaited composition from callback-scoped PCP payload through the real bounded FLV parser and the 0.0.9k source-bound decode generation. The decode boundary is the sole packet-submission and native-receive authority; no parallel submission machine or decoded queue is added.

Retained packet／flush relief stays inside the open PCP callback or explicit finalization operation. Each cycle performs one native receive, one awaited exact-completion settlement, completion release, and one retry-only transition. Repeated `EAGAIN` remains bounded by shared decode-operation, frame-count, and managed-byte budgets.

Ordinary receive and accepted-flush drain remain caller-driven one-frame operations outside the packet callback. Encoded finalization, independent video／audio flush acceptance, per-stream decoder EOF, and full decoder drain remain distinct states.

### Next: 0.0.9n Decoded Completion Transfer and Scheduler Ingress Review

Review the ownership seam from the 0.0.9m decoded completion into the existing bounded scheduler:

- define one-shot managed-frame detachment so accepted transfer survives source-completion disposal;
- preserve exact source／decoder-generation／discontinuity-generation／stream-local sequence identity;
- assign a separate checked generation-wide cross-media scheduler sequence only after accepted transfer;
- reject sequence commit on dropped, rejected, cancelled, or faulted transfer;
- keep scheduler-owned frame and matching native context alive until delivery, drop, or explicit cleanup ends ownership;
- define partial transfer failure and cleanup without adding a second decoded queue;
- dispose old-generation queued frames before old decode/native ownership on discontinuity;
- separate scheduler ingress from awaited delivery and output-device lifecycle;
- keep the review documentation-only before runtime transfer implementation.

### Later sequence

1. implement one-shot decoded-frame transfer and real-FLV bounded scheduler ingress;
2. add generation-matched awaited scheduler delivery and output lifecycle composition;
3. add a production generation factory and review ABI-gate acquisition／backend activation／Windows lifecycle;
4. perform controlled real-media validation.

### Deferred

- MP4／fragmented MP4 parser
- avformat integration
- production generation factory and backend activation
- real-FLV scheduler ingress／awaited delivery
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
