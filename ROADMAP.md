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

### 0.0.10d Controlled Physical Windows Video／Audio Output Validation — Deferred

#### Established

- Phase 1 evidence admission, Phase 2 native counter capture, finite Windows runner, and explicit controlled physical composition are implemented;
- corrected D3D11 COM vtable declarations restore the production upload and texture-copy paths;
- known-pattern and texture-copy physical probes were visible;
- controlled fixture video produced `Completed／Accepted`, passed `VideoAccepted`, and was visibly recognizable;
- the known 80 ms managed tone and decoded 80 ms fixture slice were recognizably audible;
- consumed readiness-gated ordinary controlled fixture-audio runs at `7fbeb8b0dabd9c6af5b2dc9d065d66fd05faf53d`, `77a947f98e7a9449523b49a39bc8b4e8d7ed430b`, and `774342a03c33713b1b7f70e2cb664397c04f8d04` each had separate operator observation `Audio audible: Yes` for a recognizable beep;
- the `7fbeb8...` run returned child exit `1`, `DeliveryRejected`, terminal rejection `PreCleanupCheckpointRejected`, and final native stage `ActivationCleanupCompleted`; it retained a sanitized failure record but no canonical completed or terminal record;
- the `77a947f...` run returned child exit `1`, `DeliveryRejected`, terminal rejection `PreCleanupTraceCardinalityRejected`, and final native stage `ActivationCleanupCompleted`; it retained a sanitized failure record but no canonical completed or terminal record;
- static source review established a two-stage ownership loss: production terminal cleanup cleared the active decoder generation, then lifecycle cleanup cleared the production owner before the terminal snapshot;
- the granular rejection was therefore a terminal-evidence retention asymmetry, not proof of an extra native audio write;
- PR #201 merged as main `7e31627151f5a6dba7fb9e3f0b03cd08be3b14cf` and retains the immutable scheduler trace across both ownership boundaries without changing physical-output behavior;
- the later `774342a...` run returned child exit `1`, stdout／stderr bytes `0／3122`, final native stage `ActivationCleanupCompleted`, controlled result `None`, and terminal rejection `None`; it retained a `755`-byte／`21`-line sanitized failure record with SHA-256 `F8B8A0553A6DDF0935BFD2C075FFE32E541659E3A13C1275130E655363B5FAF5` but no canonical completed or terminal record;
- the retained version 5 record cannot prove the private stderr classification, so the exact historical managed exception source is not established;
- PR #203 merged as main `e265a2b47731214a2f26a0b4a4227721158490b2` and prospectively converts unexpected direct managed-command failures into bounded `Faulted` output, fixed private phase diagnostics, and `ProjectionRejected` for snapshot／terminal-projection failures;
- PR #205 merged as main `46a9ff163e8b0fb4582175d4ff1d56cd753fdeb9` and adds strict child failure record v6 with one allowlisted managed phase, exact 22-line canonical shape, v2-v5 compatibility, and legacy preflight report compatibility;
- bounded scheduler delivery, WASAPI write/padding, lifecycle checkpoints, native aggregate, completed record, terminal record, and sanitized failure evidence are implemented;
- completed, terminal, child-failure, helper-failure, and readiness records have strict offline preflights;
- bounded controlled-run result and terminal-rejection retention, exact audio stream-format validation, bounded readiness failure disclosure, direct readiness child capture, granular pre-cleanup rejection categories, terminal trace retention, managed command failure bounding, and managed failure phase retention are implemented;
- non-physical readiness validation, atomic readiness capture, and the readiness-required authorization entrypoint are implemented.

#### Not established

- stable deterministic controlled audio delivery across the retained contradictory runs;
- canonical completed／accepted audio evidence from the same run as recognizable fixture audio;
- independent completed-record preflight for that same run;
- physical completion and formal audio acceptance for any recognizable run;
- paired video／audio admission;
- completion of 0.0.10d.

#### Retained blocking evidence

The one approved render-evidence helper invocation at main `4abadd280ceeae310b2528b6ea62f377211b1273` returned an unexpected helper failure, retained neither requested record, and had `Audio audible: No`. The exact trigger is unrecoverable; the confirmed defect was evidence loss. That authorization is consumed.

The separately authorized run at `7fbeb8b0dabd9c6af5b2dc9d065d66fd05faf53d` established recognizable ordinary fixture audio only as a separate operator observation. Its `DeliveryRejected` result and aggregate `PreCleanupCheckpointRejected` terminal projection mean technical completion and formal acceptance remain unestablished. That authorization is consumed.

The separately authorized run at `77a947f98e7a9449523b49a39bc8b4e8d7ed430b` reproduced the recognizable beep while narrowing the terminal rejection to `PreCleanupTraceCardinalityRejected`. Its readiness record SHA-256 was `DC27A6EDDEA5BE31DA7E816DC3B057C4B00FCB11D4F924C40DBEE43C82EE0A3B`; its sanitized failure record was `798` bytes／`21` lines with SHA-256 `E5AA6E01A55E662E18E9E70089CFDE46125153A57064BF38C2166AACB2DE2ECE`. It did not establish physical completion or formal audio acceptance. That authorization is consumed.

The later separately authorized run at `774342a03c33713b1b7f70e2cb664397c04f8d04` used readiness record SHA-256 `94EA6E584BD06392EEEC438154D93FC077D13B33DD0D058210BD030BCD8109D2`. It produced a recognizable beep but no canonical completed or terminal record. Its sanitized failure record SHA-256 was `F8B8A0553A6DDF0935BFD2C075FFE32E541659E3A13C1275130E655363B5FAF5`. The exact historical private stderr classification cannot be reconstructed. Its readiness and physical authorizations are consumed.

The helper console line `Sanitized helper failure record retained: No` refers only to the absence of an additional helper-level record after the child-capture failure record already occupied the exclusive failure path. It does not contradict the verified child failure record.

Issue #129 remains open. Historical helpers and diagnostics must not be rerun.

#### Deferral gate

No additional concrete non-physical evidence gap is currently identified after PR #205. The milestone is explicitly deferred without claiming physical completion, formal audio acceptance, paired admission, or completion. Issue #129 remains open. A later controlled physical run is a separate authorization decision; merge, documentation, CI, existing readiness evidence, fresh-looking paths, and the literal entrypoint guard do not grant it. Do not repeat readiness capture or physical output without a new explicit one-run authorization.

A future controlled physical audio render run requires:

1. a separately merged exact `main` commit;
2. final-head and post-merge CircleCI success;
3. a newly captured and independently revalidated canonical readiness record for that exact commit and operation bounds;
4. new repository-external completed, terminal, and failure paths;
5. a new explicit one-run authorization;
6. execution exactly once through `tools/Run-ControlledWindowsPhysicalAudioRenderEvidenceWithReadiness.ps1`.

The underlying helper must not be invoked directly. Readiness evidence and the literal invocation guard are not authorization.

Current-state reviews: `docs/reviews/PCN-0166-CONTROLLED-AUDIO-RENDER-AUTHORIZATION-STATE.md` and `docs/reviews/PCN-0167-MANAGED-AUDIO-FAILURE-BOUNDARY.md`.

### 0.0.10e Shared Windows Application Ownership Boundary — Complete

- move the existing application-owned Win32 window, message loop, owner thread, and bounded dispatcher from `Nova.Cli` into `Nova.Windows`;
- keep one implementation rather than copying the controlled runner ownership model;
- keep `Nova.Playback` limited to the borrowed dispatcher contract and window token;
- preserve all controlled physical command behavior and authorization boundaries;
- add `Nova.App` as the user-facing start/close/cancel composition above exactly one shared host;
- keep the default application graph free of controlled commands and FFmpeg bundling;
- project the exact bounded FFmpeg notice, provenance, and runtime-replacement
  text as an immutable legal/About model through the shared dispatcher;
- keep projection lifetime finite and fail closed without adding a binary,
  downloader, installer, runtime invocation, or application-resource owner.
- add a framework-dependent `win-x64` portable publish profile with the exact
  project and FFmpeg legal-material allowlist and no external runtime payload.
- retain that portable output as the only implemented distribution boundary;
  installer, signing, and update mechanics require separate future ADRs.

Current reviews: `docs/reviews/PCN-0170-SHARED-WINDOWS-APPLICATION-OWNERSHIP.md`,
`docs/reviews/PCN-0171-USER-FACING-WINDOWS-STARTUP-COMPOSITION.md`, and
`docs/reviews/PCN-0172-BOUNDED-LEGAL-ABOUT-PROJECTION.md`,
`docs/reviews/PCN-0173-FRAMEWORK-DEPENDENT-WINDOWS-PORTABLE-PACKAGE.md`, and
`docs/reviews/PCN-0174-INSTALLER-SIGNING-UPDATE-BOUNDARY.md`.

### Later sequence

1. implement no installer, signing, or update mechanism without a separate ADR;
2. revisit deferred 0.0.10d only through a separate reviewed authorization decision.

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
