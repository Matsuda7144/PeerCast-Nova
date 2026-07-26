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

### 0.0.10a Approved FFmpeg Candidate Manifest Refresh — Complete

The reviewed BtbN 2026-07-21 win64 LGPL shared candidate is the exact built-in manifest identity:

- active build ID `btbn.ffmpeg.20260721.win64.lgpl-shared` with fixed release, archive, BtbN, and FFmpeg identity;
- exact five load-role hashes plus complete ten-file reviewed `bin` inventory;
- complete 223-file expanded inventory and verbatim build configuration／license evidence;
- GPL／nonfree disabled and shared／static policy enforced by the manifest;
- 2026-07-15 identity and hashes retained as superseded rejection history;
- four additional Windows system dependencies classified and allow-listed without bundling;
- no FFmpeg binary included in Git, build, publish, installer, or release artifacts.

### 0.0.10b Controlled Real External-Runtime Native Decode Validation — Complete

The exact approved external runtime and retained FLV fixture completed one controlled bounded native decode validation:

- H.264 packets／frames: `10 / 10`;
- AAC packets／frames: `48 / 48`;
- decoded audio samples: `49,152`;
- decode errors: `0`;
- complete context, packet, frame, runtime, and ABI cleanup;
- padded planar／packed audio line sizes accepted while copying only logical sample bytes.

D3D11／WASAPI output was intentionally outside this milestone.

### 0.0.10c Controlled Windows Video／Audio Output Validation Review — Complete

The review selected:

- an application-owned real window, message loop, and owner-thread dispatcher;
- one finite playback-owned D3D11／DXGI／WASAPI lifecycle;
- independent video and audio physical evidence records;
- deterministic evidence plus separate operator visible／audible observations;
- no hidden retry, resident loop, device fallback, or device recreation.

### 0.0.10d Controlled Physical Windows Video／Audio Output Validation — In Progress

#### Established

- Phase 1 evidence admission, Phase 2 native counter capture, finite Windows runner, and explicit controlled physical composition are implemented;
- corrected D3D11 COM vtable declarations restore the production upload and texture-copy paths;
- known-pattern and texture-copy physical probes were visible;
- controlled fixture video produced `Completed／Accepted`, passed `VideoAccepted`, and was visibly recognizable;
- the known 80 ms managed tone and decoded 80 ms fixture slice were recognizably audible;
- bounded scheduler delivery, WASAPI write/padding, lifecycle checkpoints, native aggregate, completed record, terminal record, and sanitized failure evidence are implemented;
- completed, terminal, child-failure, helper-failure, and readiness records have strict offline preflights;
- non-physical readiness validation, atomic readiness capture, and the readiness-required authorization entrypoint are implemented.

#### Not established

- recognizable ordinary controlled fixture audio;
- stable deterministic controlled audio delivery across the retained contradictory runs;
- one independently accepted completed audio record plus separate `Audio audible: Yes` observation;
- paired video／audio admission;
- completion of 0.0.10d.

#### Retained blocking evidence

The one approved render-evidence helper invocation at main `4abadd280ceeae310b2528b6ea62f377211b1273` returned an unexpected helper failure, retained neither requested record, and had `Audio audible: No`. The exact trigger is unrecoverable; the confirmed defect was evidence loss. That authorization is consumed.

Issue #129 remains open. Historical helpers and diagnostics must not be rerun.

#### Current gate

No additional physical-output-free evidence plumbing is required before the next authorization decision.

A future controlled physical audio render run requires:

1. a separately merged exact `main` commit;
2. final-head and post-merge CircleCI success;
3. a newly captured and independently revalidated canonical readiness record for that exact commit and operation bounds;
4. new repository-external completed, terminal, and failure paths;
5. a new explicit one-run authorization;
6. execution exactly once through `tools/Run-ControlledWindowsPhysicalAudioRenderEvidenceWithReadiness.ps1`.

The underlying helper must not be invoked directly. Readiness evidence and the literal invocation guard are not authorization.

Current-state review: `docs/reviews/PCN-0166-CONTROLLED-AUDIO-RENDER-AUTHORIZATION-STATE.md`.

### Later sequence

1. resolve the one readiness-gated controlled physical audio decision and complete or explicitly defer 0.0.10d;
2. add the user-facing application window／message-loop composition without duplicating the controlled runner ownership model;
3. add the application dispatcher implementation and legal／About surface;
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
- complete synchronization／resampling／drift correction
- GUI、installer、recorder until the native/output gates are complete

## CI policy

Routine Linux CI uses CircleCI while GitHub Actions usage is exhausted. Final branch heads must pass restore, Release build with analyzers, all tests, and format verification. Windows execution remains reserved for a separately approved milestone validation and is never implied by routine CI.

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
- distinguish readiness evidence from physical authorization
- never reuse a consumed one-run authorization
- record provenance and redistribution permission for third-party assets

## 0.1.0 Genesis gate

- bounded demux and production composition
- exact built-in approval and explicit host-attested replacement boundary
- finite application-owned Windows lifecycle composition
- finite decoded buffering and output lifecycle
- controlled real native decode evidence
- controlled accepted visible video evidence
- controlled accepted and recognizable audio evidence
- paired video／audio admission
- user-facing application and packaging
