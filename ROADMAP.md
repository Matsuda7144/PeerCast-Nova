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

## 0.0.9 Source-bound Native Decoder and Packet Ownership — Complete

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
- 0.0.9y Finite Approved External Runtime and Windows Lifecycle Activation Boundary
- 0.0.9z Controlled Retained-Fixture Production Activation Validation

0.0.9u implements the dedicated production owner from awaited PCP／FLV input through source-bound decode, scheduler transfer, caller-driven delivery, explicit reset, stop, and discontinuity replacement.

0.0.9w implements the corrected external LGPL compliance gate. It approves only the exact built-in candidate, records host replacements as `HostDeclared`／`HostAttested`／`Unreviewed`, requires `AllowHostAttestedRuntime`, preserves exact runtime verification, includes readable legal／provenance／release-guard material, and adds no bundling or discovery.

0.0.9y implements the finite application lifecycle above that production entry and the existing Windows output stack. It adds the explicit dispatcher contract, pure preflight, owner-thread activation and output operations, generation-matched initial／replacement configuration, explicit retained resize, terminal device loss, production-first stop, active-operation cancellation, and partial-construction cleanup without creating a window, thread, queue, loop, retry, fallback, or FFmpeg distribution path.

0.0.9z connects controlled real FLV bytes through the real parser, production composition, source-bound decode, decoded-completion transfer, scheduler ownership, and the 0.0.9y lifecycle. It proves exact stream-derived output configuration, one receive／one transfer／one retained retry relief, caller-visible `WouldBlock`, later committed delivery, stop-old-before-create-new discontinuity replacement, output-generation reconfiguration, and production-first cleanup without physical Windows or real FFmpeg activation.

## 0.0.10 Controlled Native Activation and Output Evidence — In Progress

### Next: 0.0.10a Controlled Real External-Runtime Decode Activation Review

Define the evidence and ownership gate for the first controlled activation of the exact built-in approved external runtime:

- identify one retained media fixture with committed provenance and checksum;
- identify one exact external runtime directory matching the approved manifest;
- keep runtime binaries outside repository, release, CI artifact, installer, and cache publication;
- select an explicit Windows runner and application-owned dispatcher／window environment;
- define expected runtime verification, ABI, symbol, context-open, packet, frame, and cleanup evidence;
- define how native frame bytes and metadata are inspected without claiming physical output;
- define cancellation, failure, retained ownership, and terminal cleanup evidence;
- prohibit runtime search, download, extraction, fallback, rehosting, or silent candidate replacement;
- keep physical D3D11／WASAPI rendering and audible／visible playback in later milestones.

### Later sequence

1. implement controlled real external-runtime native decode validation;
2. review and implement controlled Windows audio／video output validation;
3. add the application window／message loop and user-facing lifecycle;
4. expand packaging only after the preceding gates pass.

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
- do not convert host attestation into Nova review approval
- keep application window and output dispatcher externally owned
- keep Windows native output resources under one owner thread
- keep output generation equal to the active decoder generation
- make device loss terminal unless a later ADR defines a new complete lifecycle
- distinguish controlled managed integration from physical native activation evidence
- record provenance and redistribution permission for third-party assets

## 0.1.0 Genesis gate

- bounded demux and production composition
- exact built-in approval and explicit host-attested replacement boundary
- finite application-owned Windows lifecycle composition
- finite decoded buffering and output lifecycle
- controlled real native decode evidence
- controlled audible／visible verification
- user-facing application and packaging
