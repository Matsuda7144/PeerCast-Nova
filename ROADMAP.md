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

0.0.9k adds one generation-owned source-bound receive authority. It performs one callback-scoped native receive and managed copy per operation, returns one exact owned decoded completion, coordinates retained packet／flush retry only after frame release, and drains accepted video／audio flushes independently to decoder EOF without a second decoded queue or resident loop.

### Next: 0.0.9l Awaited Decoded Completion Composition Review

Review the connection between the 0.0.9i awaited PCP-to-FLV composition and the 0.0.9k source-bound receive boundary. The review must define:

- replacement of the current `PlaybackPcpFlvCompositionGeneration` submission-only ownership with the 0.0.9k generation boundary;
- exact factory ownership for source session, parameter binding, ABI gates, native session, submission state, and decoded receive state;
- how retained packet／flush relief is driven inside the awaited PCP callback without returning while callback memory remains required;
- how ordinary post-accept receive and accepted-flush drain are driven outside the packet callback through finite caller operations;
- the decoded-completion consumer contract and whether managed ownership is retained, consumed, rejected, or transferred;
- composition with existing managed conversion and bounded scheduler ingress without a second decoded queue;
- shared frame-count／managed-byte budgets across callback relief and caller-driven drain;
- parser, encoded submission, decoded completion, scheduler, and native-session backpressure ordering;
- cancellation, stop, terminal failure, source disposal, and discontinuity generation replacement;
- decoded completion, scheduler ownership, submission, native session, parser, and factory cleanup order;
- scope separation from production ABI-gate acquisition, backend activation, Windows output lifecycle, and playback claims.

The review should remain documentation-only. Runtime composition and decoded delivery must be implemented and validated in later milestones.

### Later sequence

1. implement awaited PCP／FLV／decode composition using the 0.0.9k boundary;
2. add real-FLV decoded completion conversion and bounded scheduler ingress／delivery;
3. add a production generation factory and review backend activation／Windows lifecycle;
4. perform controlled real-media validation.

### Deferred

- MP4／fragmented MP4 parser
- avformat integration
- production generation factory and backend activation
- real-FLV decoded conversion／scheduler ingress／delivery
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
