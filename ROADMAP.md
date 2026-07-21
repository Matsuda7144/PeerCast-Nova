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

## 0.0.6 Decoder and Output Boundaries — Implemented; application composition pending

- verified FFmpeg runtime lease and ABI gate
- native context through packet／frame processing
- managed copy、conversion、awaited delivery
- bounded scheduler and Windows output probe／activation／binding／stream／buffer boundaries

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
- 0.0.9s Policy-gated Production Decoder Generation Factory Boundary
- 0.0.9t Production Generation Ingress and Replacement Composition Review
- 0.0.9u Finite Production Generation Ingress and Replacement Composition Boundary
- 0.0.9v FFmpeg Candidate License, Backend Registration, and Distribution Review
- 0.0.9w External LGPL Runtime Compliance and User Replacement Boundary
- 0.0.9x Approved External Runtime and Windows Lifecycle Activation Review

0.0.9u implements the dedicated production owner from awaited PCP／FLV input through source-bound decode, scheduler transfer, caller-driven delivery, explicit reset, stop, and discontinuity replacement.

0.0.9w implements the external LGPL compliance gate. It adds immutable built-in and host manifests, exact runtime verification, legal material, user replacement, and an approved application-root FFmpeg production entry without bundling or discovery.

0.0.9x reviews the missing application lifecycle above that production entry and the existing Windows output stack. It selects one application-provided owner-thread dispatcher, keeps window／message-loop ownership outside playback, retains the 0.0.9q generation-matched scheduler as the sole production delivery authority, uses the active decoder generation for output configuration, makes resize explicit, and makes device loss terminal for the first implementation.

### Next: 0.0.9y Finite Approved External Runtime and Windows Lifecycle Activation Boundary

Implement one finite lifecycle owner that:

- accepts an approved built-in or host FFmpeg manifest and one explicit runtime directory;
- accepts an application-owned output dispatcher and positive window token without owning either;
- validates backend policy and pure configuration before OS work;
- runs the resource-free Windows adapter probe;
- dispatches COM／D3D11／WASAPI activation to the owner thread;
- constructs binding, stream-initialization, and buffer-processing boundaries around the singular activation owner;
- adds a minimal dispatcher-backed process／reset output port for asynchronous production delivery;
- generalizes the generation delivery seam only enough to consume that port without adding another scheduler path;
- creates the approved external runtime production composition borrowing the output port;
- accepts input before stream configuration but blocks delivery until an exact parser stream description exists;
- binds and initializes output with the exact active decoder generation;
- resets and reconfigures output state before delivering a discontinuity replacement generation;
- preserves scheduler ownership through `WouldBlock`, timing wait, initialization, binding, and resize requirements;
- handles resize explicitly at the current generation;
- treats device loss as terminal, stops production first, then tears down output on the owner dispatcher;
- preserves the application window and dispatcher during cleanup;
- adds no playback-owned thread, queue, timer, message pump, resident loop, hidden retry, or fallback;
- proves ownership, ordering, cancellation, partial cleanup, and idempotent stop through injected cross-platform tests.

### Later sequence

1. perform controlled retained-fixture production activation validation;
2. perform controlled real-media decode validation;
3. perform controlled Windows audio／video output validation;
4. add the application window／message loop and user-facing lifecycle;
5. expand packaging only after the preceding gates pass.

### Deferred

- FFmpeg binary bundling or Nova-hosted runtime pack
- playback-owned output dispatcher or message pump
- automatic device-loss recovery／source reconnect
- MP4／fragmented MP4 parser
- avformat integration beyond the reviewed runtime inventory
- discontinuity reset and `avcodec_flush_buffers`
- duration cutoff
- independent audio recovery generation changes
- complete synchronization
- controlled audible／visible playback

## CI policy

Routine Linux CI uses CircleCI while GitHub Actions usage is exhausted. Final branch heads must pass restore, Release build with analyzers, all tests, and format verification. Windows CI remains reserved for explicit milestone or release validation.

## Platform and dependency policy

- retain `net10.0`
- avoid speculative production dependencies
- keep FFmpeg host-installed and dynamically loaded
- do not bundle, mirror, rehost, download, or install FFmpeg in the default distribution
- preserve explicit-directory and checksum／ABI verification for user replacements
- keep application window and output dispatcher externally owned
- keep Windows native output resources under one owner thread
- record provenance and redistribution permission for third-party assets

## 0.1.0 Genesis gate

- bounded demux and production composition
- approved external runtime compliance and replacement boundary
- finite application-owned Windows lifecycle composition
- finite decoded buffering and output lifecycle
- controlled audible／visible verification
- user-facing application and packaging
