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

### 0.0.10d Controlled Physical Windows Video／Audio Output Validation — Complete

#### Established

- Phase 1 evidence admission, Phase 2 native counter capture, finite Windows runner, and explicit controlled physical composition are implemented;
- corrected D3D11 COM vtable declarations restore the production upload and texture-copy paths;
- known-pattern and texture-copy physical probes were visible;
- controlled fixture video produced `Completed／Accepted`, passed `VideoAccepted`, and was visibly recognizable;
- the known 80 ms managed tone and decoded 80 ms fixture slice were recognizably audible;
- consumed readiness-gated ordinary controlled fixture-audio runs at `7fbeb8b0dabd9c6af5b2dc9d065d66fd05faf53d`, `77a947f98e7a9449523b49a39bc8b4e8d7ed430b`, `774342a03c33713b1b7f70e2cb664397c04f8d04`, `847b0ea4cff8aca48c103e499c0bf2bca3fbc6b5`, `abb071f5fcb6e78f86ad5f185387adee060e29fb`, and `e0809e9b711d5bb5101130a26ca19944a94a0d31` each had separate operator observation `Audio audible: Yes` for a recognizable beep;
- the `7fbeb8...` run returned child exit `1`, `DeliveryRejected`, terminal rejection `PreCleanupCheckpointRejected`, and final native stage `ActivationCleanupCompleted`; it retained a sanitized failure record but no canonical completed or terminal record;
- the `77a947f...` run returned child exit `1`, `DeliveryRejected`, terminal rejection `PreCleanupTraceCardinalityRejected`, and final native stage `ActivationCleanupCompleted`; it retained a sanitized failure record but no canonical completed or terminal record;
- static source review established a two-stage ownership loss: production terminal cleanup cleared the active decoder generation, then lifecycle cleanup cleared the production owner before the terminal snapshot;
- the granular rejection was therefore a terminal-evidence retention asymmetry, not proof of an extra native audio write;
- PR #201 merged as main `7e31627151f5a6dba7fb9e3f0b03cd08be3b14cf` and retains the immutable scheduler trace across both ownership boundaries without changing physical-output behavior;
- the later `774342a...` run returned child exit `1`, stdout／stderr bytes `0／3122`, final native stage `ActivationCleanupCompleted`, controlled result `None`, and terminal rejection `None`; it retained a `755`-byte／`21`-line sanitized failure record with SHA-256 `F8B8A0553A6DDF0935BFD2C075FFE32E541659E3A13C1275130E655363B5FAF5` but no canonical completed or terminal record;
- the retained version 5 record cannot prove the private stderr classification, so the exact historical managed exception source is not established;
- PR #203 merged as main `e265a2b47731214a2f26a0b4a4227721158490b2` and prospectively converts unexpected direct managed-command failures into bounded `Faulted` output, fixed private phase diagnostics, and `ProjectionRejected` for snapshot／terminal-projection failures;
- PR #205 merged as main `46a9ff163e8b0fb4582175d4ff1d56cd753fdeb9` and adds strict child failure record v6 with one allowlisted managed phase, exact 22-line canonical shape, v2-v5 compatibility, and legacy preflight report compatibility;
- the `847b0ea...` run returned child exit `1`, `DeliveryRejected`, terminal rejection `PreCleanupTraceCardinalityRejected`, and final native stage `ActivationCleanupCompleted`; it retained an `826`-byte／`22`-line sanitized failure record with SHA-256 `BF1E4BD3304142CB53737F63C1D1849BF3B67546464C53188E874E21A01E50C1` but no canonical completed or terminal record;
- static tracing established that the dispatcher-backed Windows generation path omitted `CaptureDeliveryObservations`, leaving a native audio trace with an empty scheduler trace;
- PR #213 merged as main `a6ec858162b1170cc35a5bbc94ae4068f4335090` and forwards the selected observation setting without changing physical-output or authorization behavior;
- the `abb071f...` run returned child exit `1`, exact fallback `Result: Faulted`, managed failure phase `OutcomeWrite`, and final native stage `ActivationCleanupCompleted`; it retained a `795`-byte, `22`-line sanitized failure record with SHA-256 `2A2D40B9388D8F6E679D31FBBEBFA92448ED6F052C53E08A22441A7067AFA602` but no canonical completed or terminal record;
- static review established that the fixed four-slot scheduler trace admitted video reservations from the mixed H.264/AAC fixture even though the completed audio record requires four audio observations;
- PR #215 merged as main `94878943dfbd7df2c059bf5f347bcfbf0f24f272` and limits the opt-in evidence trace to audio observations without changing physical-output or authorization behavior;
- the `e0809e9...` run ended `DeliveryRejected / None` after `4 / 3 / 2` decode, outer-delivery, and delivered counts, retained terminal SHA-256 `859651D2DED3417F4F55BBAD1E210D1B48AC9E274681D669D6CF1C516E851C78`, and again had `Audio audible: Yes`;
- static review established that a roughly `6 ms` retained wait was resumed after the fixed `21.3334 ms` cadence and became `AudioLate`, while repeated observations for one sequence could also consume multiple fixed trace slots;
- ADR-0165 and PCN-0175 use only a validated bounded observed wait in the opt-in controlled evidence path and coalesce same-sequence trace updates without changing production timing or native output;
- the `6eb3558...` run completed four deliveries and retained canonical 26-line `Completed / Accepted` audio render evidence with SHA-256 `C6CD53C053F99FF43A1514E1162D3C90FE307C5FE32285F73013A612C2FBD100`;
- that same record passed its independent completed-record preflight, and the operator reported `Audio audible: Yes` and a recognizable retained-fixture beep;
- ADR-0166 and PCN-0176 connect the strict completed render-evidence format directly to the independent paired verifier without synthesizing a legacy audio record;
- final independent paired admission completed on merged main `4f38de92ce66d988479f5e22b263de9f84b32086`, accepted both immutable retained records, executed no physical run, and preserved both input hashes;
- bounded scheduler delivery, WASAPI write/padding, lifecycle checkpoints, native aggregate, completed record, terminal record, and sanitized failure evidence are implemented;
- completed, terminal, child-failure, helper-failure, and readiness records have strict offline preflights;
- bounded controlled-run result and terminal-rejection retention, exact audio stream-format validation, bounded readiness failure disclosure, direct readiness child capture, granular pre-cleanup rejection categories, terminal trace retention, managed command failure bounding, and managed failure phase retention are implemented;
- non-physical readiness validation, atomic readiness capture, and the readiness-required authorization entrypoint are implemented.

#### Retained historical evidence

The one approved render-evidence helper invocation at main `4abadd280ceeae310b2528b6ea62f377211b1273` returned an unexpected helper failure, retained neither requested record, and had `Audio audible: No`. The exact trigger is unrecoverable; the confirmed defect was evidence loss. That authorization is consumed.

The separately authorized run at `7fbeb8b0dabd9c6af5b2dc9d065d66fd05faf53d` established recognizable ordinary fixture audio only as a separate operator observation. Its `DeliveryRejected` result and aggregate `PreCleanupCheckpointRejected` terminal projection mean technical completion and formal acceptance remain unestablished. That authorization is consumed.

The separately authorized run at `77a947f98e7a9449523b49a39bc8b4e8d7ed430b` reproduced the recognizable beep while narrowing the terminal rejection to `PreCleanupTraceCardinalityRejected`. Its readiness record SHA-256 was `DC27A6EDDEA5BE31DA7E816DC3B057C4B00FCB11D4F924C40DBEE43C82EE0A3B`; its sanitized failure record was `798` bytes／`21` lines with SHA-256 `E5AA6E01A55E662E18E9E70089CFDE46125153A57064BF38C2166AACB2DE2ECE`. It did not establish physical completion or formal audio acceptance. That authorization is consumed.

The later separately authorized run at `774342a03c33713b1b7f70e2cb664397c04f8d04` used readiness record SHA-256 `94EA6E584BD06392EEEC438154D93FC077D13B33DD0D058210BD030BCD8109D2`. It produced a recognizable beep but no canonical completed or terminal record. Its sanitized failure record SHA-256 was `F8B8A0553A6DDF0935BFD2C075FFE32E541659E3A13C1275130E655363B5FAF5`. The exact historical private stderr classification cannot be reconstructed. Its readiness and physical authorizations are consumed.

The helper console line `Sanitized helper failure record retained: No` refers only to the absence of an additional helper-level record after the child-capture failure record already occupied the exclusive failure path. It does not contradict the verified child failure record.

Issue #129 is closed as completed. Historical helpers and diagnostics must not be rerun.

#### Completion gate

The later separately authorized run at `847b0ea4cff8aca48c103e499c0bf2bca3fbc6b5` used readiness record SHA-256 `00861785FAD839D4DA9A86D032E6CE32CDE9F3F25774B5E364F0816EA03B318D`. It produced a recognizable beep but no canonical completed or terminal record. Its sanitized failure record SHA-256 was `BF1E4BD3304142CB53737F63C1D1849BF3B67546464C53188E874E21A01E50C1`. Its readiness and physical authorizations are consumed.

The next separately authorized run at `abb071f5fcb6e78f86ad5f185387adee060e29fb` used readiness record SHA-256 `B908FC83309F09AD58A296A55F509F868E686264E659DE0B41CDB490840B24B9`. It produced a recognizable beep but no canonical completed or terminal record. Its sanitized failure record SHA-256 was `2A2D40B9388D8F6E679D31FBBEBFA92448ED6F052C53E08A22441A7067AFA602`. Its readiness and physical authorizations are consumed.

The later accepted run at `6eb3558ec0efd434d9ee1e7221ba86d3c115684f` and final independent paired admission on merged main `4f38de92ce66d988479f5e22b263de9f84b32086` satisfy the reviewed completion conditions. Issue #129 is closed as completed. No additional physical run was needed for paired admission.

All readiness and physical-run authorizations used for the milestone are consumed. Historical commands and paths must not be reused. Any future controlled physical run is a separate reviewed authorization decision; merge, documentation, CI, existing evidence, and the literal entrypoint guard do not grant it.

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

1. implement no installer, signing, or update mechanism without a separate ADR.

### Post-0.0.10e non-physical slice

The strict opt-in decoded-buffer duration cutoff boundary is implemented by
ADR-0167 and reviewed in
`docs/reviews/PCN-0177-OPT-IN-DECODED-BUFFER-DURATION-CUTOFF.md`.

The non-native `Nova.App --help` and `Nova.App --about` information entrypoints
are implemented by ADR-0168 and reviewed in
`docs/reviews/PCN-0178-NON-NATIVE-APPLICATION-INFORMATION-ENTRYPOINT.md`.
General playback GUI and application workflows remain incomplete.

ADR-0169 and
`docs/reviews/PCN-0179-SAMPLE-DERIVED-AUDIO-DURATION-ACCOUNTING.md` add opt-in,
conservatively rounded sample-derived audio duration accounting. Unknown video
duration remains unresolved.

ADR-0170 and
`docs/reviews/PCN-0180-AUDIO-SCOPED-DECODED-DURATION-CUTOFF.md` add an explicit
audio-only duration scope while retaining count and byte bounds for video. No
default production value or scope is selected in that slice.

ADR-0171 and
`docs/reviews/PCN-0181-DEFAULT-PRODUCTION-AUDIO-DURATION-CUTOFF.md` select a
250 ms audio-only cutoff for default production generations. Complete all-media
duration handling remains incomplete.

ADR-0172 and
`docs/reviews/PCN-0182-OPENED-DECODER-DISCONTINUITY-FLUSH-BOUNDARY.md` add an
opened-context `avcodec_flush_buffers` capability with exact ABI admission and
fail-closed ownership. Production discontinuity integration and decoder
generation reuse remain incomplete.

ADR-0173 and
`docs/reviews/PCN-0183-BOUNDED-ISO-BMFF-BOX-HEADER-PARSER.md` add a bounded,
payload-free ISO BMFF box-header parser for normal, extended-size,
extends-to-end, and `uuid` headers. MP4/fMP4 traversal and payload semantics
remain incomplete.

ADR-0174 and
`docs/reviews/PCN-0184-CONTAINER-BOUNDED-ISO-BMFF-BOX-TRAVERSAL.md` add
payload-free sequential traversal inside one complete caller-provided
container. Streaming accumulation, nested-container policy, and recognized
payload semantics remain incomplete.

ADR-0175 and
`docs/reviews/PCN-0185-BOUNDED-ISO-BMFF-FILE-TYPE-BOX-PARSER.md` add bounded
projection of one complete `ftyp` box, including major/minor brand fields and a
limited read-only compatible-brand list. Brand policy and all other box payload
semantics remain incomplete.

ADR-0176 and
`docs/reviews/PCN-0186-BOUNDED-ISO-BMFF-FULL-BOX-HEADER-PARSER.md` add the
allocation-free four-byte version/flags prefix shared by ISO BMFF FullBox
payloads. Individual FullBox type, length, version, flags, and payload policies
remain incomplete.

ADR-0177 and
`docs/reviews/PCN-0187-ISO-BMFF-MOVIE-HEADER-TIMING-PROJECTION.md` add exact
version 0/1 `mvhd` creation, modification, timescale, and duration projection.
Track and sample timing semantics remain incomplete.

ADR-0178 and
`docs/reviews/PCN-0188-ISO-BMFF-MEDIA-HEADER-TIMING-LANGUAGE-PROJECTION.md` add
exact version 0/1 `mdhd` media timing and validated packed ISO-639 language
projection. Track identity, handler classification, and samples remain
incomplete.

ADR-0179 and
`docs/reviews/PCN-0189-ISO-BMFF-HANDLER-REFERENCE-CLASSIFICATION.md` add exact
version 0 `hdlr` Video/Audio/Other classification and a strictly decoded,
bounded UTF-8 handler name. Sample descriptions and codec selection remain
incomplete.

ADR-0180 and
`docs/reviews/PCN-0190-ISO-BMFF-TRACK-HEADER-IDENTITY-PROJECTION.md` add exact
version 0/1 `tkhd` track identity, defined flags, movie-timeline duration, and
raw 16.16 dimensions. Track composition and presentation-transform policy
remain incomplete.

ADR-0181 and
`docs/reviews/PCN-0191-BOUNDED-ISO-BMFF-SAMPLE-DESCRIPTION-ENTRY-LIST.md` add
bounded version 0 `stsd` sample-entry count and exact entry-range projection.
Visual/audio sample-entry payloads and codec configuration remain incomplete.

ADR-0182 and
`docs/reviews/PCN-0192-BOUNDED-ISO-BMFF-TIME-TO-SAMPLE-TABLE.md` add bounded
version 0 `stts` decode-time runs with checked total sample and duration units.
Timestamp expansion and the remaining sample tables remain incomplete.

ADR-0183 and
`docs/reviews/PCN-0193-BOUNDED-ISO-BMFF-COMPOSITION-TIME-TABLE.md` add bounded
version 0/1 `ctts` composition-offset runs with exact unsigned/signed
projection. Decode/composition reconciliation remains incomplete.

ADR-0184 and
`docs/reviews/PCN-0194-BOUNDED-ISO-BMFF-SAMPLE-SIZE-TABLE.md` add bounded
version 0 `stsz` uniform and individual sample-size projection with exact total
sample bytes. Sample-to-chunk and media-data association remain incomplete.

ADR-0185 and
`docs/reviews/PCN-0195-BOUNDED-ISO-BMFF-SAMPLE-TO-CHUNK-TABLE.md` add bounded
version 0 `stsc` sample-to-chunk run projection with exact ordering and nonzero
field validation. Chunk offsets, lookup, and cross-table reconciliation remain
incomplete.

ADR-0186 and
`docs/reviews/PCN-0196-BOUNDED-ISO-BMFF-CHUNK-OFFSET-TABLE.md` add bounded
version 0 `stco` unsigned 32-bit chunk-offset projection without premature
range or ordering policy. `co64`, lookup, and media-data validation remain
incomplete.

ADR-0187 and
`docs/reviews/PCN-0197-BOUNDED-ISO-BMFF-LARGE-CHUNK-OFFSET-TABLE.md` add
bounded version 0 `co64` unsigned 64-bit chunk-offset projection without
truncation or premature range policy. Table selection, lookup, and media-data
validation remain incomplete.

ADR-0188 and
`docs/reviews/PCN-0198-BOUNDED-ISO-BMFF-SYNC-SAMPLE-TABLE.md` add bounded
version 0 `stss` nonzero, strictly increasing sync-sample projection.
Sample-count reconciliation, keyframe seek, and packet projection remain
incomplete.

ADR-0189 and
`docs/reviews/PCN-0199-BOUNDED-ISO-BMFF-COMPACT-SAMPLE-SIZE-TABLE.md` add
bounded version 0 `stz2` 4／8／16-bit compact sample-size projection with exact
packed length and total bytes. Table selection and cross-table reconciliation
remain incomplete.

ADR-0190 and
`docs/reviews/PCN-0200-BOUNDED-ISO-BMFF-SAMPLE-DEPENDENCY-TYPE-TABLE.md` add
bounded ISO-form version 0 `sdtp` typed sample-dependency projection with
payload-derived entry count. Sample-count reconciliation and packet projection
remain incomplete.

ADR-0191 and
`docs/reviews/PCN-0201-BOUNDED-ISO-BMFF-PARTIAL-SYNC-SAMPLE-TABLE.md` add
bounded version 0 `stps` nonzero, strictly increasing partial-sync sample
projection. Full-sync integration, seek policy, and reconciliation remain
incomplete.

ADR-0192 and
`docs/reviews/PCN-0202-ISO-BMFF-SAMPLE-COUNT-SIZE-TABLE-RECONCILIATION.md` add
exact `stts` timing-to-`stsz`／`stz2` sample-count reconciliation with exclusive
size-table selection and total-byte projection. Remaining sample and chunk
tables are not yet reconciled.

ADR-0193 and
`docs/reviews/PCN-0203-ISO-BMFF-COMPOSITION-SAMPLE-COUNT-RECONCILIATION.md`
extend that boundary with optional `ctts` count agreement and presence
projection. Composition timestamp expansion and offset arithmetic remain
incomplete.

ADR-0194 and
`docs/reviews/PCN-0204-ISO-BMFF-DEPENDENCY-SAMPLE-COUNT-RECONCILIATION.md`
extend reconciliation with optional `sdtp` entry-count agreement and presence
projection. Dependency interpretation and packet flag projection remain
incomplete.

ADR-0195 and
`docs/reviews/PCN-0205-ISO-BMFF-SYNC-SAMPLE-RANGE-RECONCILIATION.md` extend
reconciliation with optional `stss` one-based sample-number range validation
and table-presence projection. Seek and keyframe behavior remain incomplete.

ADR-0196 and
`docs/reviews/PCN-0206-ISO-BMFF-PARTIAL-SYNC-SAMPLE-RANGE-RECONCILIATION.md`
extend reconciliation with optional `stps` one-based sample-number range
validation and table-presence projection. Full-sync integration and seek
behavior remain incomplete.

ADR-0197 and
`docs/reviews/PCN-0207-ISO-BMFF-CHUNK-TABLE-RECONCILIATION.md` add exclusive
`stco`／`co64` selection and `stsc` run coverage validation against the chunk
count. Sample descriptions and media-data bounds remain incomplete.

ADR-0198 and
`docs/reviews/PCN-0208-ISO-BMFF-CHUNK-SAMPLE-COUNT-RECONCILIATION.md` add
bounded run-length sample-count projection from `stsc` and optional exact
agreement with the reconciled sample count. Per-chunk expansion remains
incomplete.

ADR-0199 and
`docs/reviews/PCN-0209-ISO-BMFF-SAMPLE-DESCRIPTION-INDEX-RECONCILIATION.md`
add optional explicit `stsd` agreement for every one-based
`sample_description_index` in `stsc`. Sample-entry payload semantics and
decoder selection remain incomplete.

ADR-0200 and
`docs/reviews/PCN-0210-ISO-BMFF-BOUNDED-CHUNK-RUN-LOOKUP.md` add fail-closed
binary lookup from a one-based chunk number to its reconciled `stsc` run.
Media-data association remains incomplete.

ADR-0201 and
`docs/reviews/PCN-0211-ISO-BMFF-BOUNDED-CHUNK-OFFSET-LOOKUP.md` add
fail-closed one-based chunk-offset lookup with reconciled `stco`／`co64` kind
and count validation. Media-data bounds and reads remain incomplete.

ADR-0202 and
`docs/reviews/PCN-0212-ISO-BMFF-BOUNDED-SAMPLE-CHUNK-LOOKUP.md` add bounded
run-length lookup from a one-based sample number to its chunk, chunk-local
index, and sample-description index. Media-data association remains incomplete.

ADR-0203 and
`docs/reviews/PCN-0213-ISO-BMFF-BOUNDED-SAMPLE-SIZE-LOOKUP.md` add fail-closed
one-based sample-size lookup with reconciled `stsz`／`stz2` kind, count, and
total-byte validation. Media-data bounds remain incomplete.

ADR-0204 and
`docs/reviews/PCN-0214-ISO-BMFF-BOUNDED-SAMPLE-BYTE-RANGE-LOOKUP.md` compose
the reconciled chunk, offset, and size lookups into overflow-checked absolute
sample byte ranges. File and media-data bounds remain incomplete.

ADR-0205 and
`docs/reviews/PCN-0215-ISO-BMFF-BOUNDED-SAMPLE-FILE-BOUNDS-VALIDATION.md` add
exact sample-range consistency and unsigned 64-bit file-length containment,
including zero-size end-of-file behavior. Media-data bounds remain incomplete.

ADR-0206 and
`docs/reviews/PCN-0216-ISO-BMFF-BOUNDED-SAMPLE-MEDIA-DATA-CONTAINMENT.md` add
overflow-checked containment of a sample range within one caller-selected
`mdat` payload. Automatic multi-`mdat` association remains incomplete.

ADR-0207 and
`docs/reviews/PCN-0217-ISO-BMFF-BOUNDED-SAMPLE-MEDIA-DATA-LOOKUP.md` add
bounded top-level structural traversal that locates the `mdat` payload
containing one sample range. Payload reads and packet projection remain
incomplete.

ADR-0208 and
`docs/reviews/PCN-0218-ISO-BMFF-BOUNDED-SAMPLE-DECODE-TIME-LOOKUP.md` add
run-relative lookup of one sample's decode start, duration, and exclusive end
in media time units without per-sample expansion. Timescale conversion and
composition offsets remain incomplete.

ADR-0209 and
`docs/reviews/PCN-0219-ISO-BMFF-BOUNDED-SAMPLE-COMPOSITION-OFFSET-LOOKUP.md`
add optional-table-aware lookup of one sample's version-correct composition
offset without per-sample expansion. Decode-time addition and timescale
conversion remain incomplete.

ADR-0210 and
`docs/reviews/PCN-0220-ISO-BMFF-EXACT-SAMPLE-PRESENTATION-TIME-LOOKUP.md` compose
decode start and optional composition offset into signed `Int128` presentation
start and exclusive end units. Timescale conversion and edit-list policy
remain incomplete.

ADR-0211 and
`docs/reviews/PCN-0221-ISO-BMFF-BOUNDED-MEDIA-TIME-CONVERSION.md` add
overflow-free `Int128` media-unit conversion through the `mdhd` timescale with
explicit toward-zero tick rounding and bounded output magnitude. Interval and
edit-list policy remain incomplete.

ADR-0212 and
`docs/reviews/PCN-0222-ISO-BMFF-BOUNDED-SAMPLE-PRESENTATION-INTERVAL-CONVERSION.md`
compose exact sample presentation units and bounded media-time conversion into
managed start, exclusive end, and endpoint-derived duration. Edit-list policy
remains incomplete.

ADR-0213 and
`docs/reviews/PCN-0223-BOUNDED-ISO-BMFF-EDIT-LIST-PARSER.md` add bounded
version 0/1 `elst` projection with exact signed media times, raw 16.16 rate
components, empty-edit identification, and checked segment-duration aggregate.
Edit application policy remains incomplete.

ADR-0214 and
`docs/reviews/PCN-0224-BOUNDED-ISO-BMFF-EDIT-SEGMENT-LOOKUP.md` add bounded
movie-time lookup of one edit entry with exclusive segment bounds, exact
relative offset, zero-duration skipping, and empty-edit reporting. Movie/media
timescale and rate-aware application remain incomplete.

ADR-0215 and
`docs/reviews/PCN-0225-EXACT-NORMAL-RATE-ISO-BMFF-EDIT-TIME-MAPPING.md` compose
edit-segment lookup with parsed movie/media timescales for exact normal-rate
point mapping. Empty edits, non-normal rates, and non-integral media-unit
results fail explicitly. Interval overlap and sample selection remain
incomplete.

ADR-0216 and
`docs/reviews/PCN-0226-ISO-BMFF-EDIT-TRACK-DURATION-RECONCILIATION.md` require
the checked `elst` segment-duration aggregate to equal a known `tkhd` duration
in their shared movie timescale. Unknown duration is not inferred. Movie-header
comparison and sample selection remain incomplete.

ADR-0217 and
`docs/reviews/PCN-0227-EXACT-NORMAL-RATE-ISO-BMFF-EDIT-INTERVAL-PROJECTION.md`
project one indexed, non-empty, normal-rate edit into exact half-open movie and
media intervals. Non-integral duration conversion fails explicitly. Sample
overlap and admission remain incomplete.

ADR-0218 and
`docs/reviews/PCN-0228-BOUNDED-ISO-BMFF-SAMPLE-EDIT-OVERLAP-LOOKUP.md` compare
one exact sample presentation interval with all supported edit intervals,
preserving bounded full or partial media intersections across repeated edits.
Movie-time overlap projection and partial-sample admission remain incomplete.

ADR-0219 and
`docs/reviews/PCN-0229-EXACT-RATIONAL-ISO-BMFF-SAMPLE-EDIT-MOVIE-OVERLAP.md`
project bounded media overlaps onto exact rational movie-timeline bounds without
rounding, while preserving repeated appearances and integral-boundary metadata.
Partial-sample admission remains incomplete.

ADR-0220 and
`docs/reviews/PCN-0230-COMPLETE-SAMPLE-ISO-BMFF-EDIT-ADMISSION.md` add a
conservative policy that retains all complete sample appearances and explicitly
rejects partial-only samples without rounding rational movie bounds. Packet
selection and production demux composition remain incomplete.

ADR-0221 and
`docs/reviews/PCN-0231-BOUNDED-ISO-BMFF-EDITED-SAMPLE-ADMISSION-LOOKUP.md`
compose numbered-sample presentation lookup, bounded edit overlap, exact
rational movie projection, and complete-sample admission behind one fail-closed
metadata API. Byte-range association and production demux remain incomplete.

ADR-0222 and
`docs/reviews/PCN-0232-BOUNDED-ISO-BMFF-EDITED-SAMPLE-BYTE-RANGE.md` bind the
admitted sample number to the existing reconciled chunk/size byte-range lookup,
preventing cross-sample association. Top-level `mdat` containment and production
packet composition remain incomplete.

ADR-0223 and
`docs/reviews/PCN-0233-BOUNDED-ISO-BMFF-EDITED-SAMPLE-MEDIA-DATA.md` compose the
typed edited-sample byte range with bounded top-level `mdat` containment while
preserving admission and sample identity. Payload ownership and production
packet composition remain incomplete.

ADR-0224 and
`docs/reviews/PCN-0234-BOUNDED-ISO-BMFF-EDITED-SAMPLE-PAYLOAD-VIEW.md` revalidate
the recorded `mdat` location against current file bytes and expose the exact
sample as a non-owning span without allocation.

ADR-0225 and
`docs/reviews/PCN-0235-BOUNDED-OWNED-ISO-BMFF-EDITED-SAMPLE-PAYLOAD.md` promote
that validated view to private owned memory under the existing 4 MiB demux
packet hard limit. Demux-event and codec-specific composition remain incomplete.

ADR-0226 and
`docs/reviews/PCN-0236-ISO-BMFF-EDITED-SAMPLE-PACKET-CANDIDATE.md` bind owned
sample bytes, exact media presentation units, media timescale, and complete edit
appearances without inferring codec or stream identity. Sample-description and
codec-specific demux-event projection remain incomplete.

ADR-0227 and
`docs/reviews/PCN-0237-ISO-BMFF-EDITED-SAMPLE-DESCRIPTION-SELECTION.md` retain
the resolved `stsc` description index with the edited range, select that exact
`stsd` entry, classify `avc1`／`avc3`／`mp4a`, and require a matching video/audio
handler. Codec-specific sample-entry payload parsing remains incomplete.

ADR-0228 and
`docs/reviews/PCN-0238-BOUNDED-ISO-BMFF-VISUAL-SAMPLE-ENTRY.md` parse the common
`avc1`／`avc3` VisualSampleEntry prefix, validate fixed fields, and project a
bounded exact child-box sequence. AVC configuration parsing remains incomplete.

ADR-0229 and
`docs/reviews/PCN-0239-BOUNDED-ISO-BMFF-AUDIO-SAMPLE-ENTRY.md` parse the common
version-zero `mp4a` AudioSampleEntry prefix, preserve its raw 16.16 sample rate,
and project a bounded exact child-box sequence. `esds` parsing remains
incomplete.

ADR-0230 and
`docs/reviews/PCN-0240-BOUNDED-ISO-BMFF-AVC-DECODER-CONFIGURATION.md` require a
unique current `avcC`, bound owned configuration bytes and parameter-set counts,
and parse base plus high-profile decoder-configuration fields exactly. AVC
packet framing remains incomplete.

ADR-0231 and
`docs/reviews/PCN-0241-BOUNDED-ISO-BMFF-MPEG4-DESCRIPTOR-HEADER.md` parse the
shared one-to-four-byte MPEG-4 descriptor tag/length prefix with the existing
codec-extradata hard limit and exact or container-prefix consumption modes.
`esds` hierarchy semantics remain incomplete.

ADR-0232 and
`docs/reviews/PCN-0242-BOUNDED-ISO-BMFF-AVC-SAMPLE-FRAMING.md` bind the selected
`avcC` NAL length width to the exact owned edited-sample payload, require the
same sample-description index, and project a bounded exact sequence of
non-owning NAL-unit ranges. Annex-B conversion, parameter-set injection,
demux-event projection, and production MP4 demux composition remain incomplete.

ADR-0233 and
`docs/reviews/PCN-0243-BOUNDED-ISO-BMFF-ELEMENTARY-STREAM-DESCRIPTOR.md` require
a unique current version-zero `esds`, own its bounded outer ES descriptor, and
project exact optional ES fields plus a bounded direct-child hierarchy with
unique ordered decoder-config and SL-config descriptors. Decoder-config body,
decoder-specific AAC configuration, and SL-config semantics remain incomplete.

ADR-0234 and
`docs/reviews/PCN-0244-BOUNDED-ISO-BMFF-DECODER-CONFIG-DESCRIPTOR.md` parse the
audio decoder-config fixed body, preserve buffer and bitrate metadata, traverse
bounded nested descriptors, and require one nonempty decoder-specific-info
payload. AudioSpecificConfig and SL-config body semantics remain incomplete.

ADR-0235 and
`docs/reviews/PCN-0245-BOUNDED-ISO-BMFF-AUDIO-SPECIFIC-CONFIG-HEADER.md` parse
the leading MPEG-4 AudioSpecificConfig bit fields, including extended Audio
Object Type and indexed or explicit sampling frequency, while preserving
channel configuration zero for later semantics. AAC extension,
GASpecificConfig, Program Config Element, and channel-layout semantics remain
incomplete.

ADR-0236 and
`docs/reviews/PCN-0246-BOUNDED-ISO-BMFF-GA-SPECIFIC-CONFIG.md` continue from the
exact AudioSpecificConfig header bit position and project conditional General
Audio frame, core-coder, scalable, ER BSAC, and resilience fields. Program
Config Element, SBR/PS extension, and channel-layout semantics remain
incomplete.

ADR-0237 and
`docs/reviews/PCN-0247-BOUNDED-ISO-BMFF-SL-CONFIG-DESCRIPTOR.md` bind the owned
SLConfigDescriptor to the exact one-byte predefined MP4 configuration `2` and
reject malformed, foreign, custom, or other predefined forms. Custom SL
configuration and timestamp scheduling remain incomplete.

ADR-0238 and
`docs/reviews/PCN-0248-BOUNDED-ISO-BMFF-EXPLICIT-SBR-PS-EXTENSION.md` parse
leading Audio Object Type `5`／`29` explicit SBR／PS signalling, including the
extension frequency, core object type, and ER BSAC extension channel
configuration. Implicit sync-extension search remains incomplete.

ADR-0239 and
`docs/reviews/PCN-0249-BOUNDED-ISO-BMFF-EXPLICIT-EXTENSION-GA-COMPOSITION.md`
continue from the exact explicit-extension ending bit into the existing bounded
GASpecificConfig projection while retaining both source objects and the
effective core type. Program Config Element and implicit sync-extension
semantics remain incomplete.

ADR-0240 and
`docs/reviews/PCN-0250-BOUNDED-ISO-BMFF-IMPLICIT-SBR-SYNC-EXTENSION.md` consume
the immediate optional `0x2B7` marker after supported direct non-ER
GASpecificConfig, distinguish absence from truncation, and project SBR presence
plus bounded extension frequency. ER post-configuration and arbitrary-position
sync handling remain incomplete.

ADR-0241 and
`docs/reviews/PCN-0251-BOUNDED-ISO-BMFF-IMPLICIT-PS-SYNC-EXTENSION.md` consume
the optional immediate `0x548` marker after present implicit SBR and project its
PS presence bit with exact source and bit-position provenance. Decoder and
channel-layout semantics remain incomplete.

ADR-0242 and
`docs/reviews/PCN-0252-BOUNDED-ISO-BMFF-AVC-NAL-UNIT-CLASSIFICATION.md` classify
the exact framed sample NAL headers without payload copies, project reference
class and type, and expose VCL, IDR, and parameter-set presence. RBSP and slice
semantics remain incomplete.

ADR-0243 and
`docs/reviews/PCN-0253-BOUNDED-ISO-BMFF-AVC-PARAMETER-SET-PREFIX-PLAN.md`
validate `avcC` SPS／PPS／SPS-extension NAL types and length representability,
then project a count- and byte-bounded non-owning sequence before the classified
sample units. Byte materialization and production injection policy remain
incomplete.

ADR-0244 and
`docs/reviews/PCN-0254-BOUNDED-ISO-BMFF-AVC-LENGTH-PREFIXED-PAYLOAD-MATERIALIZATION.md`
revalidate the prefix plan and materialize privately owned configuration-plus-
sample bytes using the exact selected one-to-four-byte big-endian NAL length
width under the 4 MiB packet hard limit. Automatic injection policy remains
incomplete.

ADR-0245 and
`docs/reviews/PCN-0255-BOUNDED-ISO-BMFF-AVC-ANNEX-B-PAYLOAD-MATERIALIZATION.md`
materialize the same ordered plan with an exact four-byte Annex-B start code
before every NAL unit under the same packet limit. Decoder-format selection and
automatic injection policy remain incomplete.

ADR-0246 and
`docs/reviews/PCN-0256-BOUNDED-ISO-BMFF-AVC-SAMPLE-ONLY-ANNEX-B-MATERIALIZATION.md`
materialize only the classified sample NAL units with exact four-byte Annex-B
start codes, preserving a separate path for decoders configured out of band.
Automatic format and injection decisions remain incomplete.

ADR-0247 and
`docs/reviews/PCN-0257-EXPLICIT-ISO-BMFF-AVC-PAYLOAD-SELECTION.md` require the
caller to select original or parameter-set-prefixed length form, or sample-only
or parameter-set-prefixed Annex-B form, while enforcing exact classification
identity and projecting format facts. Automatic decoder-state policy remains
incomplete.

ADR-0248 and
`docs/reviews/PCN-0258-EXPLICIT-ISO-BMFF-AVC-DECODER-PAYLOAD-REQUIREMENTS.md`
map caller-stated Annex-B and in-band-parameter-set requirements exactly onto
the four explicit payload forms without runtime or sample-entry inference.
Runtime capability discovery remains incomplete.

ADR-0249 and
`docs/reviews/PCN-0259-BOUNDED-ISO-BMFF-AVC-PAYLOAD-COMPOSITION.md` compose one
classification and resolved requirement through only the necessary prefix-plan,
materialization, and source-bound selection steps under shared count and packet
limits. Production decoder and demux integration remain incomplete.

ADR-0250 and
`docs/reviews/PCN-0260-BOUNDED-ISO-BMFF-GA-SPECIFIC-CONFIG-PREFIX.md` project the
exact General Audio prefix for direct and explicit-SBR/PS core configurations,
including the optional core-coder delay and Program Config Element start.
Program Config Element body and later GA semantics remain incomplete.

ADR-0251 and
`docs/reviews/PCN-0261-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-ELEMENT-HEADER.md` project
the fixed Program Config Element identity, frequency, and bounded element counts
from that exact start. Mixdown flags, channel-element lists, byte alignment,
comments, channel-layout derivation, and GA continuation remain incomplete.

ADR-0252 and
`docs/reviews/PCN-0262-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-ELEMENT.md` traverse the
conditional mixdown fields, bounded channel／data／CC element records, zero
alignment, and privately owned length-prefixed comment. Channel-layout
derivation, cross-record semantic validation, and GA continuation remain
incomplete.

ADR-0253 and
`docs/reviews/PCN-0263-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-CHANNEL-TOPOLOGY.md`
project SCE／CPE／LFE channel counts and reject duplicate element identities
without selecting speaker positions. Speaker mapping, mixdown application, and
decoder composition remain incomplete.

ADR-0254 and
`docs/reviews/PCN-0264-BOUNDED-ISO-BMFF-PCE-AWARE-GA-SPECIFIC-CONFIG-COMPOSITION.md`
resume object-type-specific GA fields after the exact PCE for channel
configuration zero, or after the shared prefix otherwise. ER
post-configuration sync-extension and decoder composition remain incomplete.

ADR-0255 and
`docs/reviews/PCN-0265-BOUNDED-ISO-BMFF-ER-AUDIO-POST-CONFIG.md` project
supported ER Audio `epConfig` values zero and one after the exact GA boundary.
Error Protection Specific Config for values two／three and subsequent sync
extensions remain incomplete.

ADR-0256 and
`docs/reviews/PCN-0266-BOUNDED-ISO-BMFF-ER-IMPLICIT-SBR-PS-SYNC-EXTENSION.md`
reuse the immediate bounded implicit SBR／PS rules from the exact supported ER
post-config position. Error Protection Specific Config, non-immediate marker
search, and decoder composition remain incomplete.

ADR-0257 and
`docs/reviews/PCN-0267-BOUNDED-ISO-BMFF-AAC-CORE-FORMAT-FACTS.md` resolve the
declared AAC sampling frequency and channel count from indexed configuration or
exact PCE topology. Effective SBR／PS output format, speaker mapping, extradata,
and decoder composition remain incomplete.

ADR-0258 and
`docs/reviews/PCN-0268-BOUNDED-ISO-BMFF-AAC-EXTENSION-SIGNALLING-FACTS.md`
resolve absent, explicit, or source-bound implicit SBR／PS without mixing forms,
and select the signalled extension frequency only for present SBR. PS output
channel policy, extradata, and decoder composition remain incomplete.

ADR-0259 and
`docs/reviews/PCN-0269-BOUNDED-ISO-BMFF-AAC-DECODER-FORMAT-INTENT.md` project
effective AAC sampling and channel-count intent, allowing PS only to promote a
mono core to stereo. Speaker masks, capability validation, extradata, and
decoder composition remain incomplete.

ADR-0260 and
`docs/reviews/PCN-0270-BOUNDED-ISO-BMFF-AAC-DECODER-EXTRADATA.md` copy the exact
AudioSpecificConfig bytes into size-bounded owned storage attached to the
decoder-format intent. FFmpeg padding, capability validation, native allocation,
and decoder binding remain incomplete.

ADR-0261 and
`docs/reviews/PCN-0271-BOUNDED-ISO-BMFF-AAC-FFMPEG-CODEC-PARAMETERS-INPUT.md`
adapt the owned AAC format and extradata to the existing managed FFmpeg
codec-parameter input after enforcing its sample-rate and channel-count limits.
Factory execution, native transfer, and decoder binding remain incomplete.

ADR-0262 and
`docs/reviews/PCN-0272-BOUNDED-ISO-BMFF-AAC-MANAGED-CODEC-PARAMETERS.md`
invoke the existing managed factory and own its exact 64-byte-zero-padded AAC
lease with ISO BMFF provenance. Native parameter transfer, decoder context
mutation, and decoder open remain incomplete.

ADR-0263 and
`docs/reviews/PCN-0273-BOUNDED-ISO-BMFF-AAC-PARAMETER-TRANSFER-PLAN.md`
retain that managed owner in an exact AAC transfer plan and project the five
symbols required by the established FFmpeg parameter-transfer boundary without
resolving or invoking them. Capability binding, native transfer, decoder context
mutation, and decoder open remain incomplete.

ADR-0264 and
`docs/reviews/PCN-0274-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-CHANNEL-SLOT-MAP.md`
expand validated PCE elements into contiguous source-bound front, side, back,
and LFE channel slots while preserving element identity and pair order. Physical
speaker positions, platform masks, and decoder layout binding remain incomplete.

ADR-0265 and
`docs/reviews/PCN-0275-BOUNDED-ISO-BMFF-AAC-TRANSFER-SYMBOL-COVERAGE.md`
validate a finite caller-declared symbol-name set against the exact five-symbol
requirement from ADR-0263 while retaining plan ownership. Verified runtime capability,
ABI/version checks, address resolution, and native invocation remain incomplete.

ADR-0266 and
`docs/reviews/PCN-0276-BOUNDED-ISO-BMFF-AAC-DECLARED-DECODER-CAPABILITY-BINDING.md`
bind that coverage and plan to bounded caller-declared AAC codec, sample-rate,
and channel limits. Verified runtime evidence, decoder discovery, native transfer,
context mutation, and decoder open remain incomplete.

ADR-0267 and
`docs/reviews/PCN-0277-BOUNDED-ISO-BMFF-ER-AUDIO-PROTECTION-REQUIREMENT.md`
project every ER `epConfig` value and identify values two and three as requiring
Error Protection Specific Config without parsing or skipping that structure.
Its bounded body parser and subsequent continuation remain incomplete.

ADR-0268 and
`docs/reviews/PCN-0278-BOUNDED-ISO-BMFF-AAC-MANAGED-DECODER-PREPARATION.md`
compose the existing AAC intent, owned extradata, padded parameters, transfer
plan, declared-symbol coverage, and declared capability binding behind one
fail-closed disposable owner. Verified runtime binding and all native decoder
execution remain incomplete.

ADR-0269 and
`docs/reviews/PCN-0279-BOUNDED-ISO-BMFF-AAC-SAMPLE-ENTRY-CONSISTENCY-FACTS.md`
retain both `mp4a` declared sample-rate/channel facts and effective AAC decoder
facts while reporting their agreement dimensions separately. Mismatch admission,
normalization, and decoder-layout policy remain incomplete.

ADR-0270 and
`docs/reviews/PCN-0280-EXPLICIT-ISO-BMFF-AAC-SAMPLE-ENTRY-CONSISTENCY-POLICY.md`
require callers to choose exact agreement or explicitly admit effective AAC
decoder facts on mismatch, retaining whether an override was used. Automatic
profile policy selection and decoder-layout binding remain incomplete.

ADR-0271 and
`docs/reviews/PCN-0281-BOUNDED-ISO-BMFF-AAC-SAMPLE-ENTRY-MANAGED-DECODER-PREPARATION.md`
apply that explicit consistency decision before composing the existing managed
AAC decoder preparation, retaining exact source identities behind one disposable
owner. Verified runtime binding, native parameter transfer, decoder integration,
and physical execution remain incomplete.

ADR-0272 and
`docs/reviews/PCN-0282-EXPLICIT-ISO-BMFF-PROGRAM-CONFIG-SPEAKER-POSITION-ASSIGNMENT.md`
bind one caller-stated speaker position to every exact PCE channel slot, reject
duplicates and LFE disagreement, and never infer a layout from PCE syntax.
Platform layout projection, decoder binding, and native transfer remain
incomplete.

ADR-0273 and
`docs/reviews/PCN-0283-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-SPECIFIC-CONFIG-PREFIX.md`
parse only the fixed Error Protection Specific Config fields for `epConfig` two
or three and stop before the nested set/class sequence. Set traversal, the
complete config end, `directMapping`, and subsequent continuation remain
incomplete.

ADR-0274 and
`docs/reviews/PCN-0284-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-FIRST-SET-HEADER.md`
read only the first set's 6-bit class count from that exact prefix end and stop
before the first class control field. Per-class traversal, later set counts, and
complete configuration continuation remain incomplete.

ADR-0275 and
`docs/reviews/PCN-0285-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-FIRST-CLASS.md`
parse the first class through every conditional escape, concatenation, FEC,
interleave, length, rate, and CRC field while retaining its exact set source and
end position. Remaining classes, set reordering, later sets, and header
protection remain incomplete.

ADR-0276 and
`docs/reviews/PCN-0286-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-FIRST-SET.md`
traverse every class in the first set and require any reordered-output vector to
be an exact class-index permutation. Later sets, header protection, complete
configuration continuation, and decoder integration remain incomplete.

ADR-0277 and
`docs/reviews/PCN-0287-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-SECOND-SET-HEADER.md`
read the second set's 6-bit class count from the exact first-set end only when the
prefix declares at least two sets. Second-set class bodies, general later-set
traversal, and header protection remain incomplete.

ADR-0278 and
`docs/reviews/PCN-0288-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-SECOND-SET-FIRST-CLASS.md`
parse the second set's first class from the exact post-count position across the
bounded conditional class syntax while retaining second-set ownership. Remaining
classes, reordered output, later sets, and header protection remain incomplete.

ADR-0279 and
`docs/reviews/PCN-0289-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-SECOND-SET.md`
traverse every class in the second set and require any reordered-output vector
to be an exact class-index permutation. Third and later sets, header protection,
complete configuration continuation, and decoder integration remain incomplete.

ADR-0280 and
`docs/reviews/PCN-0290-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-THIRD-SET-HEADER.md`
read the third set's 6-bit class count from the exact second-set end only when
the prefix declares at least three sets. Third-set bodies, general later-set
traversal, and header protection remain incomplete.

ADR-0281 and PCN-0291 parse the third set's first class from the exact post-count
position while retaining third-set ownership. ADR-0282 and PCN-0292 traverse the
complete third set and validate reordered output as an exact permutation. Fourth
and later sets, general aggregation, and header protection remain incomplete.

ADR-0283 and PCN-0293 generalize traversal across every prefix-declared set,
retain exact indexed ownership, validate each reordered-output permutation, and
enforce an aggregate class limit. Header protection and complete configuration
continuation remain incomplete.

ADR-0284 and PCN-0294 parse the header-protection flag after the exact set
aggregate and conditionally retain its 5-bit rate and CRC-length parameters.
`directMapping` and complete AudioSpecificConfig continuation remain incomplete.

ADR-0285 and PCN-0295 compose the complete owned Error Protection Specific
Config boundary. ADR-0286 and PCN-0296 project `epConfig` three's direct-mapping
bit without admitting its false value as implemented. False-value policy and
decoder integration remain incomplete.

ADR-0287 and PCN-0297 explicitly admit enabled direct mapping only. ADR-0288 and
PCN-0298 require the exact source-bound Specific Config for `epConfig` two and an
additional admitted mapping decision for value three, while values zero and one
remain config-free. Decoder integration remains incomplete.

ADR-0289 and PCN-0299 bind that admitted ER configuration to an AAC managed
decoder preparation only when both retain the exact same GA Specific Config.
Native parameter transfer and decoder integration remain incomplete.

ADR-0290 and PCN-0300 project the exact speaker assignment to the finite
WaveFormat speaker mask and retain an explicit source-to-platform channel-order
permutation. Sample reordering, decoder binding, and native transfer remain
incomplete.

ADR-0291 and PCN-0301 validate that source-to-platform order as a nonempty exact
permutation and retain a managed-only reorder plan. Sample-buffer application,
decoder binding, and native transfer remain incomplete.

ADR-0292 and PCN-0302 bind the reorder plan to managed AAC decoder preparation
only when both retain the exact same PCE topology. Reorder application, native
decoder binding, and parameter transfer remain incomplete.

ADR-0293 and PCN-0303 apply the exact plan to a complete canonical interleaved
PCM frame using a size-bounded owned managed copy while preserving all frame
metadata. Decoder integration, native transfer, rendering, and physical
execution remain incomplete.

ADR-0294 and PCN-0304 retain the exact PCE mask and channel count as an explicit
`av_channel_layout_from_mask` transfer requirement after validating preparation
availability and mask population. Symbol coverage, native transfer, and decoder
integration remain incomplete.

ADR-0295 and PCN-0305 verify the exact explicit-layout initializer against the
bounded caller-declared symbol set while retaining source ownership. Runtime
loading, address and ABI checks, native transfer, and decoder integration remain
incomplete.

ADR-0296 and PCN-0306 compose PCE managed decoder preparation, explicit mask
requirement, and declared-symbol coverage into one disposable owner with
deterministic partial-failure cleanup. Runtime loading, ABI validation, native
transfer, and decoder execution remain incomplete.

ADR-0297 and PCN-0307 continue immediate implicit SBR and PS parsing from the
exact admitted ER configuration end across `epConfig` zero through three,
including complete protection syntax and direct mapping where required.
Non-immediate search and decoder integration remain incomplete.

ADR-0298 and PCN-0308 compose that admission and optional immediate SBR/PS into
source-bound AAC core, extension-signalling, and effective decoder-format intent
while rejecting pre-admission or mismatched extension facts. Decoder preparation
and native integration remain incomplete.

ADR-0299 and PCN-0309 bind that exact admitted ER AAC composition to the
existing managed decoder preparation, retaining the admission, immediate SBR/PS
facts, effective format, owned padded parameters, transfer plan, declared-symbol
coverage, and declared capability binding behind one disposable owner. This
completes the current non-physical managed-only AAC metadata-to-preparation
chain. Runtime verification, native transfer, decoder integration, and physical
execution remain incomplete.

ADR-0328 and
`docs/reviews/PCN-0338-CALLER-BOUNDED-ISO-BMFF-IMPLICIT-SBR-SYNC-EXTENSION-SEARCH.md`
add caller-bounded one-bit-step `0x2B7` search after exact direct-GA or admitted
ER configuration, retain exact marker provenance, distinguish exhaustion from
limit exhaustion, and continue into the existing bounded SBR and immediate PS
parsers. Automatic default selection remains intentionally absent; decoder
integration and physical execution remain incomplete.

ADR-0329 and
`docs/reviews/PCN-0339-CALLER-EXPLICIT-ISO-BMFF-IMPLICIT-SBR-SYNC-EXTENSION-RESOLUTION-POLICY.md`
require a factory-validated caller choice between exact immediate-only parsing
and ADR-0328 caller-bounded search for direct GA or admitted ER configuration.
The unified result retains exact policy and optional search provenance without
introducing an automatic default. Decoder integration and physical execution
remain incomplete.

ADR-0330 and
`docs/reviews/PCN-0340-BOUNDED-ISO-BMFF-TRACK-CODEC-PACKET-SEQUENCE.md`
own at most 131,072 already composed packets from one exact movie/track/media/
handler source with strictly increasing nonzero sample numbers and deterministic
sequence indexes. Sample-table iteration, automatic packet construction, and
cross-track merging remain incomplete.

ADR-0331 and
`docs/reviews/PCN-0341-BOUNDED-ISO-BMFF-TRACK-CODEC-PACKET-SEQUENCE-CURSOR.md`
add a pure immutable cursor over ADR-0330 with exact one-entry advance,
remaining-count metadata, and explicit exhaustion. Acknowledgement, automatic
packet construction, cross-track merging, and delivery remain incomplete.

ADR-0332 and
`docs/reviews/PCN-0342-EXACT-ISO-BMFF-TRACK-CODEC-PACKET-SEQUENCE-ACKNOWLEDGEMENT.md`
retain one exact ADR-0331 source cursor, entry, and one-step successor as
immutable transition metadata. Delivery, consumption, automatic packet
construction, and cross-track merging remain incomplete.

ADR-0333 and
`docs/reviews/PCN-0343-BOUNDED-ISO-BMFF-MOVIE-TRACK-CODEC-PACKET-SEQUENCE-SET.md`
own at most 256 exact same-movie ADR-0330 sequences in caller order with unique
track IDs and deterministic stream indexes. Explicit sequence selection,
packet-head comparison, and cross-track merging remain incomplete.

ADR-0334 and
`docs/reviews/PCN-0344-EXPLICIT-ISO-BMFF-MOVIE-TRACK-CODEC-PACKET-SEQUENCE-SELECTION.md`
retain caller-explicit video and/or audio stream indexes from one exact ADR-0333
set, reject invalid roles without scanning or fallback, and permit explicit
single-media selection. Cursor creation, packet-head comparison, and
cross-track merging remain incomplete.

ADR-0335 and
`docs/reviews/PCN-0345-BOUNDED-ISO-BMFF-SELECTED-TRACK-CODEC-PACKET-SEQUENCE-CURSOR-SET.md`
create immutable initial ADR-0331 cursors for exactly the ADR-0334 selected
video and/or audio roles. Any selected cursor failure rejects the whole result
without partial cursor ownership. Cursor advance, packet-head comparison, and
cross-track merging remain incomplete.

ADR-0336 and
`docs/reviews/PCN-0346-EXACT-ISO-BMFF-SELECTED-SEQUENCE-PACKET-HEAD-SNAPSHOT.md`
project the exact current entry from each selected ADR-0335 cursor without
advancing it, preserve absent or exhausted roles explicitly, and reject source,
position, or entry-index mismatches without a partial snapshot. Packet-head
time comparison, winner selection, and cross-track merging remain incomplete.

ADR-0337 and
`docs/reviews/PCN-0347-EXACT-ISO-BMFF-SELECTED-SEQUENCE-HEAD-MOVIE-TIME-COMPARISON.md`
compare the first complete movie appearance of current video/audio heads by
exact rational start and then end using overflow-safe cross multiplication.
Single-role and exhausted states remain explicit. Stream-index tie-breaking,
advance-role selection, and cross-track merging remain incomplete.

ADR-0338 and
`docs/reviews/PCN-0348-CALLER-EXPLICIT-ISO-BMFF-SEQUENCE-ADVANCE-ROLE-SELECTION.md`
select the earlier or only current role and require caller-explicit video/audio
preference for equal intervals. The exact comparison, preference, cursor, head,
and leading appearance remain immutable metadata. Cursor advance, successor
cursor-set construction, and cross-track merging remain incomplete.

ADR-0339 and
`docs/reviews/PCN-0349-EXACT-ISO-BMFF-SELECTED-SEQUENCE-CURSOR-SET-ADVANCE.md`
advance exactly the ADR-0338 selected cursor by one ADR-0331 entry, retain the
exact ADR-0332 transition, and construct an immutable successor cursor set while
preserving the non-selected role cursor by reference. Repeated merge traversal,
bounded merged output ownership, consumption, and delivery remain incomplete.

ADR-0340 and
`docs/reviews/PCN-0350-EXACT-ISO-BMFF-CROSS-TRACK-SEQUENCE-MERGE-STEP.md`
project one caller-indexed cross-track step from the exact ADR-0339 transition,
retaining stream, entry, packet, leading appearance, and successor cursor-set
provenance under the combined 262,144-step limit. Repeated traversal, contiguous
index and cursor-chain validation, and merged-list ownership remain incomplete.

ADR-0341 and
`docs/reviews/PCN-0351-BOUNDED-ISO-BMFF-CROSS-TRACK-MERGE-STEP-SEQUENCE.md`
own up to 262,144 caller-provided ADR-0340 steps with contiguous indexes, one
exact equal-interval policy, and reference-exact successor-to-source cursor
chaining. Automatic traversal and exhaustive-completion proof remain incomplete.

ADR-0342 and
`docs/reviews/PCN-0352-CALLER-BOUNDED-ISO-BMFF-CROSS-TRACK-MERGE-TRAVERSAL.md`
compose ADR-0336 through ADR-0341 into a pure managed packet merge under one
caller tie policy and finite step bound. Exact exhaustion and caller truncation
remain distinct, including already-exhausted zero-step input. Repeated edit
appearance expansion, delivery, scheduling, and production composition remain
incomplete.

ADR-0343 and
`docs/reviews/PCN-0353-EXACT-ISO-BMFF-MERGE-TRAVERSAL-CONTINUATION.md`
continue from one exact truncated final cursor state under the same tie policy
and remaining global bound. Each new segment uses local indexes, retains the
cumulative starting index, and does not re-emit prior steps.

ADR-0344 and
`docs/reviews/PCN-0354-EXACT-ISO-BMFF-PACKET-MERGE-COMPLETION.md`
certify packet-level completion only when the source is exhausted, initial and
final role sequences match exactly, final role cursors are exhausted, and the
root initial remaining total equals cumulative steps. Repeated edit-appearance
and presentation-event completeness remain incomplete.

ADR-0345 and
`docs/reviews/PCN-0355-EXACT-ISO-BMFF-COMPLETED-MERGE-STEP-VIEW.md`
walk the exact initial and continuation traversal chain into one contiguous
global-index view while retaining every original merge step and exact cursor
provenance. Edit-appearance expansion and presentation-event order remain
incomplete.

ADR-0346 and
`docs/reviews/PCN-0356-BOUNDED-ISO-BMFF-COMPLETED-MERGE-EVENT-PROJECTION.md`
expand every complete edit appearance from every ADR-0345 packet under a caller
event bound and own one immutable overflow-safe exact rational movie-time order.
Event cursor traversal, acknowledgement, delivery, and scheduling remain
incomplete.

ADR-0347 and
`docs/reviews/PCN-0357-BOUNDED-ISO-BMFF-COMPLETED-MERGE-EVENT-CURSOR.md`
traverse one exact ADR-0346 projection through an immutable one-event cursor
with explicit remaining and exhaustion state. The source projection and cursor
are never mutated.

ADR-0348 and
`docs/reviews/PCN-0358-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-ACKNOWLEDGEMENT.md`
retain one exact source cursor, event, and one-index successor triple over the
same projection. This is managed transition provenance, not consumption or
delivery evidence.

ADR-0349 and
`docs/reviews/PCN-0359-CALLER-BOUNDED-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL.md`
compose exact one-event cursor transitions into a caller-bounded immutable
acknowledgement chain with explicit exhaustion versus truncation.

ADR-0350 and
`docs/reviews/PCN-0360-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-CONTINUATION.md`
continue from one exact truncated final cursor under the remaining global event
bound without re-emitting prior acknowledgements.

ADR-0351 and
`docs/reviews/PCN-0361-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-COMPLETION.md`
certify whole root-remaining event traversal only when cumulative count and the
exact exhausted final cursor agree.

ADR-0352 and
`docs/reviews/PCN-0362-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-ACKNOWLEDGEMENT-VIEW.md`
reconstruct one contiguous global-index acknowledgement view across the exact
initial and continuation segments without copying events or transitions.
Consumption, delivery, and scheduling remain incomplete.

ADR-0353 and
`docs/reviews/PCN-0363-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-TIME.md`
project every exact ADR-0352 event into rational seconds using the shared exact
movie timescale while retaining acknowledgement provenance.

ADR-0354 and
`docs/reviews/PCN-0364-CALLER-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-CLOCK-TIME.md`
apply one nonzero caller-explicit clock rate with `BigInteger` numerators and no
rounding or clock acquisition. Integral clock policy, origin, deadlines,
consumption, delivery, and scheduling remain incomplete.

ADR-0355 through ADR-0358 and PCN-0365 through PCN-0368 add caller-explicit
integral rounding, signed origin, bounded deadline metadata, and pure
caller-tick pending/ready/expired observation to the completed traversal chain.
Clock acquisition, repeated observation, eligibility, dispatch, consumption,
delivery, and scheduling remain incomplete.

ADR-0359 and ADR-0360 with PCN-0369 and PCN-0370 add caller-explicit late-event
eligibility and caller-bounded source-ordered candidate selection to the
completed traversal chain. Target identity, requests, runtime access, dispatch,
consumption, and delivery remain incomplete.

ADR-0361 through ADR-0363 and PCN-0371 through PCN-0373 add opaque caller media
target identities, exact media-kind candidate binding, and bounded immutable
packet/payload/deadline request metadata. Capability resolution, runtime lookup,
invocation, consumption, and delivery remain incomplete.

ADR-0364 and ADR-0365 with PCN-0374 and PCN-0375 add bounded caller-declared
target media/payload capabilities and exact source-ordered request resolution.
Runtime registration and lookup, invocation, consumption, and delivery remain
incomplete.

ADR-0366 and ADR-0367 with PCN-0376 and PCN-0377 add bounded opaque runtime
target registrations and exact identity-only lookup. Runtime object access,
invocation, consumption, and delivery remain incomplete.

ADR-0368 with PCN-0378 adds one caller-identified source-ordered immutable
invocation intent projection. Runtime object access, invocation, admission,
consumption, and delivery remain incomplete.

ADR-0369 and ADR-0370 with PCN-0379 and PCN-0380 add caller-explicit exact
admission classification and a caller-bounded source-ordered admitted subset.
Runtime object access, invocation, consumption, and delivery remain incomplete.

ADR-0371 with PCN-0381 begins the production-composition foundation with a
caller-bounded contiguous sample-number plan over exact reconciled sample-count
metadata. Edit admission, payload lookup/copy, and packet construction remain
incomplete.

ADR-0372 and ADR-0373 with PCN-0382 and PCN-0383 project exact source-ordered
edit-admission classifications for every planned sample and retain a bounded
admitted subset. Byte-range/`mdat` lookup, payload copy, and packet construction
remain incomplete.

ADR-0374 with PCN-0384 resolves every selected admission to an exact absolute
byte range and sample-description index through reconciled chunk and size
metadata. File/`mdat` containment, payload copy, and packet construction remain
incomplete.

ADR-0375 through ADR-0377 with PCN-0385 through PCN-0387 add bounded exact
`mdat` containment, independently owned source-ordered payload copies, and
exact packet candidates bound to the original timing and edit appearances.
Codec composition and packet construction remain incomplete.

ADR-0378 and ADR-0379 with PCN-0388 and PCN-0389 select the exact `stsd` entry
for every packet candidate and revalidate and parse its `mp4a`, `avc1`, or
`avc3` audio/visual sample entry under one bounded child-box limit. Codec-
specific child selection, payload composition, and packet construction remain
incomplete.

ADR-0380 with PCN-0390 parses every exact selected entry's unique `avcC` or
`esds` codec configuration under caller-bounded parameter-set, descriptor, and
configuration-byte limits. Managed AVC/AAC composition and packet construction
remain incomplete.

ADR-0381 through ADR-0383 with PCN-0391 through PCN-0393 apply the existing
bounded managed AVC/AAC packet boundaries to every exact source sample and bind
the resulting packets to exact track identity, media timing, handler, and movie
timing. Multi-track collection, selection, merge, and production delivery remain
incomplete for this chain.

ADR-0384 through ADR-0387 with PCN-0394 through PCN-0397 convert exact
movie-track bindings into existing immutable per-track sequences, collect a
bounded caller-ordered same-movie unique-track set, apply caller-explicit
video/audio stream indexes, and create exact index-zero cursors. Connecting this
projection chain to bounded merge traversal and production delivery remains
incomplete.

ADR-0388 through ADR-0390 with PCN-0398 through PCN-0400 connect the projected
initial cursor set to caller-policy bounded merge traversal, exact truncated
continuation, and exact exhaustion/cumulative-count certification. Completed
step reconstruction, event projection, and production delivery remain
incomplete for this chain.

ADR-0391 through ADR-0393 with PCN-0401 through PCN-0403 reconstruct one exact
global completed-step view, project caller-bounded overflow-safe rational event
order, and create an immutable index-zero event cursor. Event traversal,
acknowledgement, clock projection, and production delivery remain incomplete for
this chain.

ADR-0394 through ADR-0396 with PCN-0404 through PCN-0406 connect that exact
projected event cursor to caller-bounded acknowledgement traversal, exact
truncated continuation without replay, and exact root-count/final-exhaustion
certification. Flattened acknowledgement views, clock projection, and
production delivery remain incomplete for this chain.

ADR-0397 with PCN-0407 reconstructs one exact contiguous global
acknowledgement view across the projected initial and continuation traversal
segments. Clock projection and production delivery remain incomplete for this
chain.

ADR-0398 through ADR-0400 with PCN-0408 through PCN-0410 bind exact rational
seconds to that projected acknowledgement view, apply a caller-explicit clock
rate with unbounded integer numerators, and apply caller-explicit integral
rounding policies. Clock-origin projection and production delivery remain
incomplete for this chain.

ADR-0401 and ADR-0402 with PCN-0411 and PCN-0412 apply one caller-explicit
signed clock origin without reading a clock and project exact immutable ready
and exclusive-expiration deadlines. Current-tick observation and production
delivery remain incomplete for this chain.

ADR-0403 through ADR-0408 with PCN-0413 through PCN-0418 classify those
deadlines against one caller tick, apply caller-explicit late eligibility,
select caller-bounded source-order candidates, attach distinct opaque media
target IDs, bind candidates exactly, and project bounded immutable packet and
deadline request metadata. Capability resolution, runtime access, invocation,
and production delivery remain incomplete for this chain.

ADR-0409 through ADR-0412 with PCN-0419 through PCN-0422 attach caller-declared
target/media/payload capabilities, resolve exact requests, attach opaque
runtime-target identity and generation records, and perform identity-only
same-target/same-media lookup. Runtime object access, invocation intent, and
production delivery remain incomplete for this chain.

ADR-0413 through ADR-0415 with PCN-0423 through PCN-0425 add caller-identified
immutable invocation-intent metadata, caller-explicit admitted/denied
classification, and caller-bounded admitted source-order selection. Runtime
object access, target calls, actual invocation, and production delivery remain
intentionally incomplete for this chain.

ADR-0416 with PCN-0426 starts a separate managed fragmented-MP4 foundation by
parsing strict FullBox-v0 `trex` track defaults with exact length and nonzero
track/sample-description identities. `mvex` traversal, movie fragments, track
fragments, fragment runs, sample-default and data-offset resolution, streaming,
and production fragmented demux remain incomplete.

ADR-0417 with PCN-0427 parses strict FullBox-v0 `mfhd` sequence headers while
leaving cross-fragment continuity policy explicit and unimplemented. `moof`
traversal and remaining fragmented sample/run composition remain incomplete.

ADR-0418 through ADR-0420 with PCN-0428 through PCN-0430 parse strict bounded
`tfhd`, `tfdt`, and `trun` leaf boxes. Optional track-fragment defaults remain
presence-aware, decode time remains lossless across versions zero and one, and
run entries are limited to 4,096 by default and 65,536 at the hard boundary.
At this leaf-parser stage, fragment traversal, inherited-default and absolute
data-offset resolution, media-data lookup, streaming, and production fragmented
demux remained incomplete.

ADR-0421 with PCN-0431 resolves one parsed run's sample defaults through exact
`trun`, `tfhd`, and `trex` precedence with track/count validation and immutable
output. Fragment traversal, decode/presentation timestamp accumulation, absolute
data-offset and byte-range resolution, media-data lookup, streaming, and
production fragmented demux remain incomplete.

ADR-0422 with PCN-0432 projects one resolved run's exact decode and presentation
times from `tfdt`, using arbitrary-precision accumulation and preserving signed
composition offsets. Timescale conversion, cross-fragment continuity, absolute
data-offset and byte-range resolution, media-data lookup, traversal, streaming,
and production fragmented demux remain incomplete.

ADR-0423 through ADR-0425 with PCN-0433 through PCN-0435 resolve explicit
fragment data origins, project arbitrary-precision contiguous sample ranges,
and join them with exact fragment timing under track/description/count/index/
size validation. Timescale conversion, cross-fragment continuity, `mdat`
admission, payload access, traversal, streaming, and production fragmented demux
remain incomplete.

ADR-0426 with PCN-0436 binds one nonzero media timescale to the checked fragment
metadata and preserves arbitrary-precision decode, presentation, and duration
numerators without rounding. Clock conversion, cross-track or cross-fragment
ordering, `mdat` admission, payload access, traversal, streaming, and production
fragmented demux remain incomplete.

ADR-0427 with PCN-0437 adds exact same-fragment presentation ordering with
decode-time and source-index tie breaks plus source-index/timescale validation.
Cross-track or cross-fragment merge, clock conversion, `mdat` admission, payload
access, traversal, streaming, and production fragmented demux remain incomplete.

ADR-0428 through ADR-0432 with PCN-0438 through PCN-0442 classify exact `mfhd`
sequence and same-track decode continuity, bind both dimensions, validate
bounded exact-endpoint transition chains, and summarize discontinuities without
selecting reset policy. Fragment discovery/traversal, reset, clock, scheduling,
cross-track sample merge, `mdat` admission, payload access, streaming, and
production fragmented demux remain incomplete.

ADR-0433 through ADR-0435 with PCN-0443 through PCN-0445 bind exact
`mfhd`/timed-run/rational-order batches, validate their one-to-one continuity
transition chain, and produce a bounded exact cross-fragment presentation merge.
ADR-0436 through ADR-0438 with PCN-0446 through PCN-0448 add an immutable
initial/resumed cursor, exact one-sample cursor step, and preflight-bounded
exhaustive traversal with contiguous same-source index validation.
ADR-0439 with PCN-0449 revalidates the exhaustive traversal's exact source,
sample, cursor-identity, index, and count chain as immutable completion metadata.
ADR-0440 and ADR-0441 with PCN-0450 and PCN-0451 reconstruct bounded immutable
acknowledgement metadata from that completion and bind it to the exact source;
this records metadata observation only and does not claim delivery.
ADR-0442 and ADR-0443 with PCN-0452 and PCN-0453 project exact
arbitrary-precision decode, presentation, and duration timing over the retained
media-timescale denominator and bind it back to that acknowledgement source.
ADR-0444 and ADR-0445 with PCN-0454 and PCN-0455 convert both decode and
presentation intervals to a caller-explicit tick rate with caller-explicit
floor, ceiling, or exact rounding and bind the policy back to the rational source.
ADR-0446 and ADR-0447 with PCN-0456 and PCN-0457 add one caller-explicit signed
arbitrary-precision origin to both integral timelines and bind the rebuilt
timeline projection back to the exact integral source without reading a clock.
ADR-0448 and ADR-0449 with PCN-0458 and PCN-0459 expose immutable signed ready
and exclusive-expiration deadline metadata for both decode and presentation
intervals and bind it back to the exact clock-origin source without observation.
ADR-0450 and ADR-0451 with PCN-0460 and PCN-0461 independently classify both
deadline dimensions as pending, ready, or expired at one caller-explicit signed
tick and bind the snapshot back to its exact deadline source without clock reads.
ADR-0452 and ADR-0453 with PCN-0462 and PCN-0463 apply independently explicit
ready-only or ready-or-expired policies to decode and presentation states and
bind the resulting facts back to the exact observation source without selection.
ADR-0454 and ADR-0455 with PCN-0464 and PCN-0465 combine those facts under a
caller-explicit require-both or require-either rule, retain a caller-bounded
source-order prefix, and bind total/truncation facts back to the exact source.
ADR-0456 and ADR-0457 with PCN-0466 and PCN-0467 project selected candidates as
target-free track/fragment/sample/deadline and exact byte-range request metadata
and bind it back to the exact source without reading payload bytes.
ADR-0458 and ADR-0459 with PCN-0468 and PCN-0469 create one caller-explicit
nonempty opaque consumer identity and bind every target-free request to that
exact identity under source, order, candidate, and track validation without
runtime lookup or dispatch.
ADR-0460 and ADR-0461 with PCN-0470 and PCN-0471 create one caller-explicit
nonempty opaque batch identity and project exact consumer bindings as a bounded
immutable source-ordered batch without scheduler, runtime, or delivery meaning.
ADR-0462 and ADR-0463 with PCN-0472 and PCN-0473 project exact batch entries as
bounded immutable submission-intent metadata and attach caller-explicit
descriptive include/exclude facts without authorization, selection, runtime
lookup, invocation, or dispatch.
ADR-0464 with PCN-0474 retains a caller-bounded source-order prefix of exact
included intents with total and truncation facts without authorization,
payload access, invocation, or dispatch.
ADR-0465 with PCN-0475 flattens selected intents into exact immutable
consumer/batch/track/fragment/sample/deadline and byte-range manifest metadata
without payload access, authorization, runtime lookup, invocation, or dispatch.
ADR-0466 with PCN-0476 binds that manifest through its selection,
classification, and intent chain to the exact original consumer batch without
payload access, authorization, runtime lookup, invocation, or dispatch.
ADR-0467 and ADR-0468 with PCN-0477 and PCN-0478 derive declarative common
identity, byte-envelope, and deadline summary facts and partition exact manifest
entries into bounded contiguous fragment batch/sequence segments without packet
composition, payload access, runtime lookup, invocation, or dispatch.
ADR-0469 and ADR-0470 with PCN-0479 and PCN-0480 add a bounded immutable cursor
over those segments and exact one-segment advancement without source mutation,
acknowledgement, completion claims, payload access, invocation, or dispatch.
ADR-0471 and ADR-0472 with PCN-0481 and PCN-0482 add caller-bounded exhaustive
segment cursor traversal and exact metadata-only completion certification with
resumed and terminal support and no acknowledgement, payload access,
authorization, invocation, dispatch, or delivery completion claim.
ADR-0473 with PCN-0483 reconstructs exact traversed segment references and
resumed source indexes as a non-acknowledging completed view without payload
access, authorization, invocation, dispatch, or delivery completion claims.
Fragment discovery, cross-track merge, delivery attempt/outcome, discontinuity
handling, clock, scheduling, `mdat` admission, payload access, streaming, and
production fragmented demux remain incomplete.

### Current managed ISO BMFF packet-to-event chain

ADR-0300 through ADR-0327 and PCN-0310 through PCN-0337 form one cumulative
managed-only chain:

- ADR-0300 projects exact AVC-selected or AAC-original compressed bytes with
  edited-sample timing and sample-description identity.
- ADR-0301 binds the packet to exact `tkhd`, `mdhd`, and `hdlr` track metadata.
- ADR-0302 binds the track packet to exact `mvhd` timing and validates its
  rational complete-sample edit appearances.
- ADR-0303 snapshots at most 256 distinct-track current packets under one exact
  `mvhd` source and assigns caller-ordered zero-based stream indexes.
- ADR-0304 retains caller-explicit video and/or audio stream selection without
  default-track inference or fallback.
- ADR-0305 projects bounded source-ordered event lists from each selected
  current packet's complete edit appearances.
- ADR-0306 merges those events into an exact rational movie-time order with
  overflow-safe comparison and deterministic ties.
- ADR-0307 projects that order into exact rational seconds by retaining each
  numerator and multiplying its denominator by the exact `mvhd` timescale.
- ADR-0308 owns those exact event times in a bounded immutable queue snapshot
  with deterministic zero-based sequence indexes.
- ADR-0309 traverses that snapshot with an immutable bounded cursor and pure
  one-entry advance that reports exhaustion explicitly.
- ADR-0310 binds the exact source cursor, queue entry, and one-step successor
  into immutable acknowledgement metadata without claiming delivery.
- ADR-0311 projects the bounded queue into caller-rate exact rational clock
  ticks with overflow-safe `BigInteger` numerators and no rounding.
- ADR-0312 converts those rationals to integral intervals only under
  caller-explicit start/end rounding modes and rejects empty results.
- ADR-0313 adds a caller-explicit signed `BigInteger` clock origin without
  reading a clock or creating deadlines.
- ADR-0314 projects those timeline intervals into immutable ready and exclusive
  expiration deadline metadata without observing a clock.
- ADR-0315 classifies all deadlines as pending, ready, or expired at one
  caller-explicit signed tick with exact boundary behavior.
- ADR-0316 applies caller-explicit ready-only or ready-plus-expired eligibility
  without selecting or dispatching an event.
- ADR-0317 selects a caller-bounded source-ordered eligible prefix with total
  eligible count and truncation metadata.
- ADR-0318 owns at most one opaque caller-explicit video and audio target
  identity without storing an invocable object.
- ADR-0319 binds exact candidates to matching media target identities without
  target resolution or invocation.
- ADR-0320 exposes each exact binding as bounded immutable request metadata with
  target ID, media kind, packet payload, and deadline.
- ADR-0321 owns explicit bounded target media and maximum-payload capability
  declarations without an invocable target.
- ADR-0322 resolves requests to matching exact capabilities by target ID, media
  kind, and payload limit without runtime lookup or invocation.
- ADR-0323 owns opaque runtime-instance IDs and nonzero generations without
  retaining runtime object references.
- ADR-0324 binds capability resolutions to exact runtime registrations by
  target ID and media kind without instance access or invocation.
- ADR-0325 projects those lookups into one caller-identified, source-indexed
  immutable invocation-intent batch without instance access or invocation.
- ADR-0326 classifies every exact intent with a caller-explicit admitted or
  denied disposition without granting runtime access or invoking a target.
- ADR-0327 selects a caller-bounded source-ordered admitted subset while
  retaining total-admitted count and explicit truncation metadata.

The current result is metadata for one current packet per selected track. It is
not a production demux loop. Default rounding and origin policies, clock
acquisition, repeated observation, actual dispatch, cursor mutation, delivery
outcome, runtime instance access and invocation, scheduling, multi-sample cursor
traversal and automatic packet composition, decoder-preparation ownership across
streams, native packet submission,
production MP4 demux composition, fragmented MP4, and streaming remain
incomplete.

### Deferred

- FFmpeg binary bundling or Nova-hosted runtime pack
- playback-owned output dispatcher or message pump
- automatic device-loss recovery／source reconnect
- MP4／fragmented MP4 streaming, nested-box policy, remaining payload
  semantics, brand policy, and decoder composition
- avformat integration beyond the reviewed runtime inventory
- production discontinuity integration and decoder-context reuse after flush
- complete all-media decoded-buffer duration policy
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
