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

0.0.9j confirms that the current state-only production receive frees a successful temporary native frame before later ownership can be established. The next implementation must replace that receive path with one sole source-bound receive authority that performs one native receive and one managed-frame copy in the same callback-scoped operation. It separates pending-send relief, ordinary post-accept receive, and independent flush drain while preserving exact source identity, one active decoded completion, finite work budgets, and decoded-frame-first cleanup.

### Next: 0.0.9k Source-bound One-frame Receive and EOS Drain Boundary

Implement one generation-owned finite boundary that:

- validates exact source session, decoder generation, stream ID, media kind, codec role, and drain state before native access;
- becomes the sole production receive authority for both source-bound contexts;
- calls `avcodec_receive_frame` at most once per public operation;
- copies one successful callback-scoped native frame into one owned managed decoded completion without a second receive;
- assigns independent checked per-stream decoded sequences only after completion construction;
- permits at most one active decoded completion across the generation;
- accepts a produced relief frame before retrying one retained packet or flush at most once;
- blocks parser input and later PCP packets while decoded or encoded ownership remains pending;
- treats ordinary post-accept `NeedMoreInput` as normal finite completion;
- treats relief or flush-drain `NeedMoreInput` as no-progress failure;
- drains accepted video and audio flushes independently one frame per call until each decoder EOF;
- bounds relief frame count, decoder advances, one-frame bytes, and cumulative managed bytes;
- handles cancellation, undefined state, metadata／data／allocation／native failures, source disposal, and discontinuity fail closed;
- disposes active decoded frame before submission state, then native session audio before video;
- adds no decoded-frame queue, resident loop, production activation, output wiring, or playback claim.

### Later sequence

1. connect source-bound managed decoded completions to conversion and bounded scheduler ingress;
2. add awaited real-FLV decoded-frame delivery and output lifecycle composition;
3. add a production generation factory and review backend activation／Windows lifecycle;
4. perform controlled real-media validation.

### Deferred

- MP4／fragmented MP4 parser
- avformat integration
- production generation factory and backend activation
- decoded-frame conversion／scheduler ingress／delivery for the real FLV path
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