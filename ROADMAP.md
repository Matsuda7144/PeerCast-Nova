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
- 0.0.9n Decoded Completion Transfer and Scheduler Ingress Review
- 0.0.9o One-shot Decoded Completion Transfer and Scheduler Ingress Boundary

0.0.9o adds explicit attached／detached／disposed ownership to the source-bound decoded completion and requires the exact active decode boundary to authorize one-shot detach. Completion disposal after detach releases decode backpressure without disposing the transferred frame.

One generation-scoped ingress validates stream-local video／audio identity, assigns a separate monotonic cross-media scheduler sequence, applies `DecoderReset` only to the first accepted replacement-generation frame, and commits counters only after accepted scheduler ownership. Cancellation before detach changes no ownership or sequence state. Rejection and contradiction are terminal, with scheduler frames disposed before matching decoder-context lifetime.

### Next: 0.0.9p Generation-matched Scheduler Delivery and Output Lifecycle Review

Review the ownership connection from the 0.0.9o scheduler ingress through the existing reservation, late-aware dispatch, conversion, awaited sink, and Windows output boundaries:

- select one generation owner for scheduler, matching decoder-context lifetime, reservations, and output work;
- define delivery／drop／late／stop／discontinuity／output-failure completion semantics;
- preserve one finite caller-driven dispatch operation rather than a resident output loop;
- dispose active decoded completion before scheduler ownership, then scheduler reservations／frames before decode／native ownership;
- prevent old-generation frames from using replacement-generation decoder contexts or output reservations;
- keep backend activation, runtime distribution, and audible／visible playback outside review scope.

### Later sequence

1. implement the reviewed generation-matched scheduler delivery and output lifecycle composition;
2. add a production generation factory and review ABI-gate acquisition／backend activation／Windows lifecycle;
3. perform controlled real-media validation.

### Deferred

- MP4／fragmented MP4 parser
- avformat integration
- production generation factory and backend activation
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
