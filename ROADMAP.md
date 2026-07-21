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
- 0.0.9r Production Decoder Generation Factory and Activation Review

0.0.9q implements one finite generation boundary that reserves one scheduler item, validates exact identity, selects the matching H.264／AAC context, performs timing and managed adaptation／conversion, maps result-aware Windows output, and completes ownership as retained, delivered, or dropped.

0.0.9r defines how production composition may build that generation. Policy must pass before runtime-path observation. The current candidate remains pending. Video and audio require independent verified runtime／ABI gate／context chains, the FLV source remains borrowed, Windows activation remains application-owned, and replacement fully stops the old generation before constructing the new one.

### Next: 0.0.9s Policy-gated Production Decoder Generation Factory Boundary

Implement one finite production generation factory that:

- evaluates candidate provenance before filesystem or native work;
- returns `LicenseReviewPending` for the current pinned candidate;
- accepts only one explicit host-installed directory and exact pinned inventory;
- creates an exact managed FLV parameter binding;
- independently acquires video and audio runtime／ABI gate chains;
- opens one complete source-bound H.264／AAC native session;
- creates one decode boundary, fresh bounded scheduler, generation timing, and 0.0.9q delivery boundary;
- returns only one complete owning generation product;
- borrows the source session and application-owned Windows buffer processor;
- unwinds partial ownership in reverse order;
- adds no resident loop, fallback runtime, download, hidden retry, or second queue.

### Later sequence

1. review and implement controlled production composition around the factory;
2. make an explicit license／backend registration and distribution decision;
3. connect bounded decode ingress and finite delivery under an application-owned Windows lifecycle;
4. perform controlled real-media validation.

### Deferred

- MP4／fragmented MP4 parser
- avformat integration
- production license approval and backend registration
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
