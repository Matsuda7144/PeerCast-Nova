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
- 0.0.9p Generation-matched Scheduler Delivery and Output Lifecycle Review
- 0.0.9q Finite Generation-matched Scheduler Delivery and Output Lifecycle Boundary

0.0.9o adds explicit attached／detached／disposed ownership to the source-bound decoded completion and requires the exact active decode boundary to authorize one-shot detach. One generation-scoped ingress validates stream-local video／audio identity, assigns a separate monotonic cross-media scheduler sequence, and commits the first accepted `DecoderReset` only after scheduler ownership.

0.0.9p selects reservation-based delivery as the production scheduler exit. It requires exact media-context selection from the same source-bound native session, a result-aware canonical output seam, typed delivered／dropped／retained reservation completion, one-shot reset admission, and asynchronous cancellation／reservation cleanup before native-context disposal. Device-wide Windows activation and stream ownership remain external.

0.0.9q implements that contract. One finite generation boundary reserves one scheduler item, validates exact identity, selects the matching H.264／AAC context, performs timing and managed adaptation／conversion, maps result-aware Windows output, and completes ownership as retained, delivered, or dropped. Replacement reset is acknowledged once, explicit video drop has separate accounting, and asynchronous stop waits for reservation release before scheduler and native decoder cleanup.

### Next: 0.0.9r Production Decoder Generation Factory and Activation Review

Review how production composition creates and replaces the completed source-bound decode／scheduler delivery generation:

- acquire the exact verified FFmpeg runtime lease and distinct video／audio ABI gates;
- enforce license and backend activation policy before native work;
- create FLV decoder parameter binding and source-bound native session without partial success;
- create decode, scheduler ingress, timing, and finite output delivery ownership as one generation;
- borrow application-owned Windows activation／binding／stream resources;
- define failure rollback and discontinuity replacement order;
- keep each construction and replacement operation finite.

### Later sequence

1. implement the reviewed production decoder generation factory;
2. connect bounded decode ingress and finite delivery through controlled production composition;
3. perform controlled real-media validation.

### Deferred

- MP4／fragmented MP4 parser
- avformat integration
- discontinuity reset and `avcodec_flush_buffers`
- duration cutoff
- independent audio recovery generation changes
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
