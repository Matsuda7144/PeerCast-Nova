# Changelog

## [Unreleased]

Detailed history through 0.0.8g remains preserved in Git history at commit
`f078dab5372a45a8c577c1c802a697ab72b45c02` and in milestone ADR/PCN records.

### Post-0.0.10e - Bounded ISO BMFF / MP4 Parser Foundation

- Added the bounded non-physical playback and application slices in ADR-0167 through ADR-0172: opt-in decoded-buffer duration cutoff, non-native `Nova.App --help` / `--about`, sample-derived audio duration accounting, audio-only and default 250 ms production duration scope, and the exact opened-decoder `avcodec_flush_buffers` boundary.
- Added the bounded ISO BMFF / MP4 parser foundation in ADR-0173 through ADR-0231, covering box headers and traversal, `ftyp`, FullBox, movie/track/media metadata, sample tables and reconciliation, chunk/sample/byte-range/`mdat` lookup, timing and edit mapping, edited-sample admission/payload/packet-candidate boundaries, `avc1`/`avc3`, `mp4a`, bounded owned `avcC`, and MPEG-4 descriptor variable-length headers.
- Established ISO BMFF / MP4 as the second supported container-parser family alongside the existing bounded FLV v1 parser.
- Added the corresponding reviews through PCN-0241 and synchronized README and ROADMAP in PR #221.
- Kept AVC packet framing, `esds` hierarchy semantics, production MP4 demux composition, nested-box and streaming policy, fragmented MP4, and general application playback incomplete.
- Audited ADR numbering and confirmed ADR-0158 was skipped by assignment error; recorded it as a permanent numbering-error gap, following ADR-0082 and ADR-0096, with no retroactive reuse.
- Changed documentation only; no source or test code was modified.

### 0.0.10d Phase 6 - Granular pre-cleanup checkpoint diagnostics

- Split the aggregate pre-cleanup terminal-record rejection into fixed delivery-trace, audio/native-trace, trace-cardinality, and trace-result-consistency categories.
- Kept the previous aggregate category readable for retained failure records while new projections emit the exact fixed category.
- Recorded the consumed one-run evidence from exact main `7fbeb8b0dabd9c6af5b2dc9d065d66fd05faf53d`: child exit `1`, `DeliveryRejected`, `PreCleanupCheckpointRejected`, final native stage `ActivationCleanupCompleted`, no completed or terminal record, and failure-record SHA-256 `C60F9E2901F710DABC80DD1C26D0E5ADCB7DCEF5C50A767F989DAF6798F604A8`.
- Recorded the separate operator observation `Audio audible: Yes` for a recognizable beep; physical completion and formal audio acceptance remain unestablished.
- Performed no additional readiness capture, physical output, probe, or diagnostic as part of this change.

### 0.0.10d Phase 6 - Bounded readiness failure reasons

- Allowed the readiness capture wrapper to republish an inner failure reason only when stderr has the exact three-line non-authorizing shape and the reason exactly matches one of the readiness script's 19 fixed gate failures.
- Captured the inner Windows PowerShell process directly through `System.Diagnostics.Process`, with concurrent private stdout/stderr reads and strict UTF-8 temp-file storage, so neither parent-shell error-record decoration nor `Start-Process` environment-key collisions can prevent validation of that exact shape.
- Kept unknown, unexpected, additional, missing, oversized, or otherwise noncanonical inner stderr behind the existing generic failure.
- Continued to suppress raw stderr, paths, command output, exceptions, and arbitrary child text.
- Added a contract test that derives every fixed inner gate reason and requires the wrapper allowlist to remain synchronized.
- Recorded the consumed one-time readiness capture from exact main `14af68e564edb4dda6b5d627f06537722bb08748`: exit `1`, generic inner-script failure, no retained record, zero physical runs, and no activation of the conditional physical authorization.
- Recorded the consumed one-time readiness capture from exact main `63e737d348e4e72185977d8e2e6245537f7e5196`: `Start-Process` failed before creating the inner readiness process, the wrapper returned its fixed unexpected-capture failure, no record was retained, zero physical runs occurred, and the conditional physical authorization did not activate.
- Performed no readiness capture, physical output, standalone preflight, probe, or diagnostic as part of this change.

### 0.0.10d Phase 6 - Fail-closed audio render format contract

- Required each audio buffer's sample format, sample rate, and channel count to exactly match the initialized WASAPI stream before any native buffer access.
- Retained the initialized audio configuration in the managed stream snapshot and cleared it on reset or rollback.
- Configured controlled retained-fixture runs for packed 32-bit float output, matching the managed conversion of the fixture's decoded planar-float AAC frames.
- Added focused tests proving that format, rate, and channel mismatches cannot reach native audio processing.
- Recorded the consumed one-run evidence from exact main `757b3ad2f429efe9add83201f2656bf284d236b9`: child exit `C0000005`, last stage `ReleaseBufferEntering`, no completed or terminal record, and operator observation `Audio audible: No` for silence.
- Performed no additional readiness capture, physical output, probe, or diagnostic as part of this change.

### 0.0.10d Phase 6 - Bounded production cleanup stage diagnostics

- Added fixed native-audio stage markers around decoder-generation, parser, and generation-factory cleanup inside the production composition stop boundary.
- Preserved the existing outer production, buffer, stream, binding, and activation cleanup markers and the exact sanitized failure-record schema.
- Routed the existing non-perturbing render-evidence reporter into production cleanup only; ordinary paths retain the null reporter.
- Added exact allowlist and production cleanup ordering tests without loading the external runtime or starting physical output.
- Recorded the consumed one-run evidence from exact main `95ed72c1e02ca73052f236c4ab93fd2ed94f037e`: child exit `C0000374`, last stage `ProductionCleanupEntering`, no completed or terminal record, and operator observation `Audio audible: No` for noise-only output.
- The new sub-stages are prospective diagnostics only and do not authorize or repeat that physical run.

### 0.0.10d Phase 6 - Bounded terminal record rejection diagnostics

- Added fixed, reviewed terminal-record rejection categories for projection, identity and bounds, runner and lifecycle state, terminal claims, pre-cleanup and post-stop checkpoints, trace counters, cleanup stability, and canonical size.
- Kept the render command stdout contract unchanged while emitting at most one exact managed rejection marker to private child stderr when terminal-record projection fails.
- Advanced the sanitized child failure record to version 5 with one `Terminal-Record-Rejection` field.
- Retained a terminal-record rejection only from exactly one canonical allowlisted marker; unknown, additional, malformed, and noncanonical stderr becomes `None`.
- Kept canonical version 4, version 3, and version 2 child failure records readable with an effective terminal-record rejection of `None`.
- Added focused codec, command, child-capture, preflight, canonical-shape, legacy-compatibility, and fail-closed parsing tests.
- Performed no physical output, readiness capture, diagnostic, probe, or authorization-entrypoint execution as part of this change.

### 0.0.10d Phase 6 - Bounded controlled run result diagnostics

- Advanced the sanitized child failure record to version 4 with one `Controlled-Run-Result` field.
- Retained a controlled run result only when private child stdout is exactly one canonical `Result: <reviewed enum>` line; unknown, additional, malformed, and noncanonical output becomes `None`.
- Preserved raw stdout and stderr privacy, exact byte counts, the bounded native cleanup stage, and all existing non-acceptance claims.
- Kept canonical version 3 and version 2 child failure records readable with an effective controlled run result of `None`.
- Added focused codec, child-capture, preflight, canonical-shape, legacy-compatibility, and fail-closed parsing tests.
- Performed no physical output, readiness capture, diagnostic, probe, or authorization-entrypoint execution as part of this change.

### 0.0.10d Phase 5 - Explicit decoder packet time-base propagation

- Added a fail-closed `av_opt_set_q` boundary that applies the exact bounded FLV stream time base to `AVCodecContext` option `pkt_timebase` after parameter transfer and before codec open.
- Added `av_opt_set_q` to the reviewed `avutil-60` ABI symbol gate without using private structure offsets.
- Retained the successfully configured packet time base in the decoder context lease and cleared it during normal or failure cleanup.
- Used the retained time base only when `AVFrame.time_base` is exactly unset (`0/positive`); a valid raw frame time base remains authoritative and malformed raw values remain rejected.
- Preserved raw PTS, best-effort timestamp, duration, generation, sequence, discontinuity, frame ownership, and existing scheduler admission semantics without timestamp synthesis or retry.
- Added focused cross-platform tests for the public option call, state and cleanup rules, strict per-stream open order, raw/effective metadata conversion, managed-copy propagation, and ABI symbol admission.
- Recorded the boundary in ADR-0144 and PCN-0152. This change does not claim a successful physical video or audio run, operator-visible video, operator-audible audio, or completion of 0.0.10d.

### 0.0.10a - Approved FFmpeg Candidate Manifest Refresh

- Replaced the active built-in identity with `btbn.ffmpeg.20260721.win64.lgpl-shared`, fixed to the 70,510,808-byte archive and SHA-256 `50764B52D38CB0BAF8AF938C0E4CA886D2C6753B520B2C319B532ED5C17E7CBF`.
- Retained the 2026-07-15 candidate identity and five DLL digests as superseded audit and rejection history.
- Fixed exact lengths and SHA-256 values for the five load-role DLLs and the complete ten-file reviewed `bin` inventory.
- Required GPL disabled, nonfree disabled, shared enabled, and static disabled for accepted manifests.
- Added `d2d1.dll`, `dwrite.dll`, `iphlpapi.dll`, and `usp10.dll` as Windows system dependency allow-list entries; none is bundled by Nova or the FFmpeg runtime.
- Retained complete `-buildconf`, `-L`, and 223-file expanded inventory evidence as text only.
- Added integrity, configuration, ABI／symbol, dependency, superseded-history, and binary-exclusion tests plus ADR-0137 and PCN-0145.
- Kept all FFmpeg archives, executables, and DLLs outside Git and build／publish artifacts.
- Performed no native DLL load, runtime ABI／symbol execution, decoder-context creation, packet submission, native decode, D3D11／WASAPI activation, or audible／visible output.
- Selected 0.0.10b Controlled Real External-Runtime Native Decode Validation.
- Completed 0.0.10b controlled native decode by accepting padded planar／packed audio line sizes in `PlaybackDecoderFfmpegFrameDataBoundary` while copying only logical sample bytes and preserving fail-closed bounds and complete cleanup.

### 0.0.9z - Controlled Retained-Fixture Production Activation Validation

- Added a controlled cross-platform integration harness around the real finite application lifecycle and real production PCP／FLV composition.
- Fed real bounded FLV bootstrap and interleaved packet bytes through the real parser-created source session and source-bound generation.
- Derived exact 320×240 video and 48 kHz stereo audio configuration from the real production snapshot and admitted output at decoder generation 0.
- Injected one audio packet `EAGAIN`, then proved exactly one receive, one managed-frame completion, one scheduler transfer, and one retained packet retry.
- Preserved one retained frame through caller-visible `RetainedWouldBlock` without a hidden delivery retry.
- Committed a later caller-driven `AudioDelivered` result and released the retained scheduler ownership exactly once.
- Validated real discontinuity replacement: generation 0 stopped completely before generation／discontinuity 1 creation.
- Reconfigured lifecycle output-ready generation from 0 to 1 and returned to `Ready` only after exact snapshot agreement.
- Preserved production-first normal stop and kept the borrowed application dispatcher and window token outside cleanup ownership.
- Kept physical Windows activation, actual ABI／runtime loading, native FFmpeg receive, application window creation, and audible／visible playback injected and unclaimed.
- Added two focused cross-platform integration tests plus ADR-0136 and PCN-0144.
- Advanced README, ROADMAP, NAVIGATION, and CHANGELOG through 0.0.9z.
- Passed routine CircleCI restore, Release build with analyzers, full solution tests, and format verification on the controlled integration head.
- Selected 0.0.10a Controlled Real External-Runtime Decode Activation Review.


### 0.0.9y - Finite Approved External Runtime and Windows Lifecycle Activation Boundary

- Added `IPlaybackWindowsOutputDispatcher` as the explicit application-owned owner-thread invocation contract without a playback-owned thread, queue, message pump, timer, or resident loop.
- Added `PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary` as one finite owner above probe, activation, binding, stream, buffer, and external-runtime production composition.
- Validated exact built-in approval or explicit host-attested policy, fixed-drive runtime-directory syntax, positive window token, canonical formats, and bounded configuration before probe, dispatcher, runtime-path, or native access.
- Routed activation, binding, initialization, processing, reset, resize, and teardown through the borrowed output dispatcher.
- Preserved the 0.0.9q generation-matched scheduler as the sole production scheduler-output authority and changed only the output execution seam.
- Blocked delivery until an exact parser stream description exists and binding, stream, and buffer snapshots match the active decoder generation.
- Added fixed initial video-bind／audio-bind／video-initialize／audio-initialize ordering.
- Added fixed replacement ordering: audio buffer reset, video buffer reset, audio stream reset, video stream reset, binding reset, video bind, audio bind, video initialize, audio initialize.
- Preserved scheduler ownership through wait, `WouldBlock`, initialization, binding, resize, audio recovery, clock-not-started, and reset-required results without hidden retry.
- Added explicit same-generation retained video resize with caller-driven later delivery.
- Made binding／stream／buffer device loss terminal for the complete lifecycle with production-first teardown and no in-place device recreation, reconnect, fallback, revival, or rollback.
- Added idempotent stop, active-operation cancellation, partial-construction cleanup, and preservation of the borrowed dispatcher and window token.
- Added copied lifecycle, generation, bounded stream-configuration, probe, activation, binding, stream, and buffer snapshots without exposing native or OS objects.
- Added ten cross-platform injected tests covering policy preflight, initial order, configuration admission, replacement order, retained resize, terminal device loss, idempotent stop, active cancellation, and activation-failure cleanup.
- Added ADR-0135 and PCN-0143 and advanced README, ROADMAP, and NAVIGATION.
- Documented narrow file-scoped CA1068, CA1859, and CA1861 rationale and restored routine CircleCI configuration after diagnostics.
- Passed routine CircleCI restore, Release build with analyzers, full solution tests, and format verification on the implementation and focused-test head.
- Selected 0.0.9z Controlled Retained-Fixture Production Activation Validation.
- Kept physical Windows output activation, application window／message loop, retained real-runtime fixture validation, real-media output, audible／visible playback, GUI, and FFmpeg binary distribution unavailable.

### 0.0.9x - Approved External Runtime and Windows Lifecycle Activation Review

- Reviewed the exact connection from the approved built-in FFmpeg production route and explicit host-attested unreviewed replacement route to the existing Windows 11 D3D11／WASAPI output boundaries.
- Confirmed that the application owns the window, message loop, and output dispatcher／owner thread.
- Kept MTA COM, D3D11 device/context, default WASAPI endpoint/client, event handle, binding, stream, and buffer ownership under one playback lifecycle owner.
- Identified that activation borrows are owner-thread-affine while production delivery resumes asynchronously, so direct concrete output calls do not prove production thread affinity.
- Selected one explicit application-provided dispatcher for every native activation, binding, initialization, processing, reset, and teardown operation.
- Prohibited a playback-owned dispatcher thread, command queue, message pump, timer, resident loop, or hidden retry.
- Kept the 0.0.9q generation-matched scheduler delivery as the sole production scheduler-output authority.
- Rejected insertion of the older 0.0.6y scheduler adapter into the production route.
- Required initial output binding and stream initialization only after an exact parser-created stream description exists.
- Required Windows binding, stream, and buffer generations to match the active decoder generation.
- Required discontinuity replacement to reconfigure output state to the new decoder generation without recreating D3D11 or WASAPI activation resources.
- Preserved scheduler ownership through `WouldBlock`, timing wait, initialization, binding, and resize requirements.
- Selected explicit same-generation video resize with caller-driven delivery retry.
- Selected terminal complete-lifecycle shutdown for device loss; no in-place device recreation, source reconnect, frame migration, generation revival, or fallback.
- Fixed normal and terminal cleanup order: stop production composition first, then dispose buffer, stream, binding, and activation on the owner dispatcher.
- Preserved the application-owned window and dispatcher through playback cleanup.
- Added ADR-0134 and PCN-0142 without changing runtime source or tests.
- Selected 0.0.9y Finite Approved External Runtime and Windows Lifecycle Activation Boundary.
- Kept physical Windows activation, controlled real-media output, audible／visible playback, GUI, and FFmpeg binary distribution unavailable.

### 0.0.9w - External LGPL Runtime Compliance and User Replacement Boundary

- Added `PlaybackFfmpegExternalRuntimeManifest` with immutable built-in and host-supplied origins and separate declaration, authorization, and review states.
- Kept only the exact built-in identity `ProjectReviewed`, `BuiltInApproved`, and `LicenseReview.Approved`; host manifests are `HostDeclared`, `HostAttested`, `Unreviewed`, and `LicenseReview.Pending`.
- Renamed the required license declaration constant to `RequiredDeclaredLicenseIdentifier` so a host declaration is not represented as Nova approval.
- Accepted only the declared string `LGPL-3.0-or-later` and exactly five reviewed FFmpeg shared-library roles.
- Required recognizable filenames, reviewed ABI majors, positive lengths, and SHA-256 digests for `avcodec`, `avformat`, `avutil`, `swresample`, and `swscale`.
- Rejected GPL, nonfree, LGPL-2.1, unknown, duplicate, missing, renamed, case-variant, path-bearing, wrong-ABI, invalid-length, and invalid-digest manifests.
- Kept runtime paths, URLs, commands, scripts, archives, download instructions, installation instructions, and fallback locations outside the manifest contract.
- Added `CreateHostSupplied` so a user or application host can attest one interface-compatible modified LGPL build without claiming Nova review.
- Added the dedicated `AllowHostAttestedRuntime` policy switch and rejected a host runtime before path observation or native work when it is absent.
- Preserved generic policy rejection of pending provenance and exact checksum verification for host replacements; later byte changes still produce a digest mismatch.
- Added `PlaybackFfmpegExternalRuntimeGenerationFactory` as a manifest-specific adapter over the existing production generation factory.
- Added `PlaybackFfmpegExternalRuntimeComposition` as the application-composition-root entry for the approved built-in or explicitly host-attested route.
- Reused the existing explicit-directory locator, retained directory and DLL handles, exact inventory verifier, PE dependency gate, ABI-major gate, and required-symbol gate.
- Kept generic backend registration, implicit discovery, network access, download, extraction, installation, update, fallback, resident pumping, second queues, and Windows resource ownership absent.
- Recorded exact BtbN release tag and commit, exact FFmpeg commit, candidate archive, and archive SHA-256 in `PROVENANCE.md`.
- Recorded that the original review did not retain verbatim `ffmpeg.exe -buildconf` and `ffmpeg.exe -L` transcripts and made those transcripts a hard gate for every future built-in approval.
- Added `THIRD-PARTY-LICENSES/FFmpeg` with prominent notice, runtime replacement instructions, readable GPLv3／LGPLv3 texts, deterministic GPLv3 gzip duplicate, fixed text hashes, provenance, and a distribution-terms guard.
- Required Nova-controlled EULA, installer, release, About, and translated terms to preserve reverse engineering for debugging LGPL-library modifications.
- Added FFmpeg trademark attribution and separated applicable license obligations from Nova's stricter no-bundling policy.
- Copied the complete FFmpeg legal directory into `Nova.Cli` build and publish output.
- Added focused manifest, host-attestation policy, replacement-integrity, legal-text, notice, provenance, release-guard, packaging, candidate-transition, and integration-probe tests.
- Updated obsolete pending-review probe expectations so the exact approved built-in candidate reaches the explicit runtime gate.
- Amended ADR-0133 and PCN-0141 and reconciled README, ROADMAP, NAVIGATION, legal policy, and provenance ledgers.
- Selected 0.0.9x Approved External Runtime and Windows Lifecycle Activation Review.
- Kept FFmpeg binary bundling／redistribution, Windows application lifecycle activation, controlled real-media decode／output validation, and audible／visible playback unavailable.

### 0.0.9v - FFmpeg Candidate License, Backend Registration, and Distribution Review

- Reviewed the exact pinned BtbN `win64-lgpl-shared` FFmpeg 8.1 candidate, archive identity, checksum, build variant, and license class.
- Confirmed that the reviewed class uses LGPLv3 shared libraries and is distinct from BtbN GPL and nonfree variants.
- Accepted external dynamic use as the permanent default architecture while keeping Nova source and binaries under Apache License 2.0.
- Prohibited FFmpeg executables, DLLs, headers, import libraries, and archives from Nova release, installer, update, and source artifacts.
- Prohibited mirroring, rehosting, automatic download, extraction, installation, update, fallback, `PATH`, registry, and implicit-directory discovery.
- Kept one explicit user／host-supplied runtime directory with exact filename, checksum, ABI-major, and required-symbol verification.
- Identified that a built-in fixed hash inventory cannot remain the only activated LGPL replacement route.
- Selected a host-owned manifest that lets users authorize an interface-compatible modified LGPL build without disabling integrity verification.
- Required activated distributions to include prominent FFmpeg notice material, verbatim GPLv3／LGPLv3 texts, and user-readable replacement instructions.
- Required Nova terms to preserve reverse engineering for debugging modifications to the LGPL library.
- Rejected production registration through generic `PlaybackDecoderBackendFactory`; selected explicit application-composition-root registration.
- Required production policy to allow third-party native code while keeping bundled code disabled.
- Recorded that a third party redistributing Nova with FFmpeg binaries is independently responsible for exact corresponding source, build configuration, patches, notices, and source availability.
- Required any future Nova-hosted runtime pack to pass a separate ADR and complete binary／source compliance archive.
- Recorded that BtbN daily asset retention is not part of Nova's runtime availability contract.
- Kept codec patent clearance outside this copyright-license architecture review.
- Added ADR-0132, PCN-0140, and `FFMPEG-EXTERNAL-RUNTIME-COMPLIANCE.md` without changing runtime source or tests.
- Kept the pinned candidate `LicenseReview.Pending` until license packaging, host-manifest validation, and replacement tests are implemented.
- Selected 0.0.9w External LGPL Runtime Compliance and User Replacement Boundary.

### 0.0.9u - Finite Production Generation Ingress and Replacement Composition Boundary

- Added `PlaybackProductionPcpFlvCompositionBoundary` as one serialized production owner from awaited PCP／FLV input through decode, scheduler transfer, caller-driven delivery, reset, and replacement.
- Kept `PlaybackPcpFlvAwaitedDecodeCompositionBoundary` unchanged for diagnostic and consumed-settlement comparison.
- Added an assembly-internal lifetime-checked forwarding bridge for exact decode snapshot, one-event submission, and one-frame receive through the singular 0.0.9s generation owner.
- Kept raw decode, native contexts, ABI gates, verified runtime leases, and Windows resources outside the public composition surface.
- Serialized packet input, explicit finalization, ordinary receive, flush drain, transfer, one-item delivery, reset, replacement, terminal cleanup, and stop through one operation gate.
- Validated decoded completion identity, generation, media, stream, sequence, frame role, and managed bytes before exactly one scheduler transfer attempt.
- Treated `Transferred` as final ownership settlement and prohibited all later completion access or disposal.
- Preserved scheduler reservations through wait, `WouldBlock`, initialization／binding／resize, audio recovery, clock-not-started, and reset-required delivery results.
- Made scheduler admission rejection terminal without capacity polling, frame reconstruction, hidden retry, or a second queue.
- Required old-generation `StopAsync` completion before parser discontinuity reset and replacement source／generation creation.
- Forbade overlapping generations, old-generation revival, rollback, and partial discontinuity-packet commit.
- Added ordered asynchronous terminal and stop cleanup while preserving the borrowed application-owned Windows output boundary.
- Added copied parser／generation／work／transfer／delivery／format snapshots without exposing native or OS resources.
- Added ten focused cross-platform tests plus ADR-0131 and PCN-0139.
- Isolated intentional CA1068, CA1822, and CA2250 contracts through SARIF status probes, added narrow file-scoped rationale, and restored routine CircleCI byte-for-byte.
- Selected 0.0.9v FFmpeg Candidate License, Backend Registration, and Distribution Review.
- Kept the pinned FFmpeg candidate `LicenseReview.Pending`, normal backend registration, FFmpeg distribution, Windows device activation, resident pumping, and audible／visible playback unavailable.

### 0.0.9t - Production Generation Ingress and Replacement Composition Review

- Reviewed the exact seam from awaited PCP／FLV parsing and source-bound decode into the complete 0.0.9s scheduler-delivery generation.
- Identified that the existing consumed-settlement composition cannot directly host production transfer because `Transferred` detaches the frame and consumes the completion.
- Prohibited post-transfer completion validation or disposal and kept the existing consumed-settlement boundary unchanged for diagnostics and comparison.
- Selected a new production-specific composition with internal lifetime-checked forwarding for decode snapshot, one-event submission, and one-frame receive through the singular generation owner.
- Required packet input, finalization, receive／drain, transfer, one-item delivery, reset, replacement, and stop to serialize through one composition operation gate.
- Kept decoded transfer one-shot and made rejected scheduler admission terminal without capacity polling, hidden retry, or a second queue.
- Kept delivery caller-driven outside the PCP callback and retained scheduler ownership through wait, output backpressure, initialization／binding／resize, audio recovery, clock, and reset requirements.
- Required asynchronous old-generation stop before parser discontinuity disposes the borrowed source session.
- Required replacement generation creation only after a new exact parser source description and forbade overlap, old-generation revival, rollback, or partial packet commit.
- Kept Windows activation／binding／stream and device recovery application-owned and required snapshots to copy only generation and parser-derived stream configuration.
- Defined one ordered asynchronous terminal cleanup path: close admission, cancel／await operation, stop generation, dispose parser／source, dispose dedicated factory, preserve borrowed output.
- Preserved the pinned FFmpeg candidate as `LicenseReview.Pending`, normal backend registration unchanged, and default production creation fail closed before runtime observation.
- Selected 0.0.9u Finite Production Generation Ingress and Replacement Composition Boundary.
- Added ADR-0130 and PCN-0138 without changing runtime source or tests.
- Kept backend activation, Windows device activation, FFmpeg distribution, continuous pumping, and audible／visible playback unavailable.

### 0.0.9s - Policy-gated Production Decoder Generation Factory Boundary

- Added `PlaybackProductionDecoderGenerationFactory` as one serialized finite construction boundary for the complete 0.0.9q generation.
- Evaluated candidate provenance and backend policy before FLV binding, runtime-path observation, ABI loading, or native work.
- Preserved the pinned FFmpeg candidate as `LicenseReview.Pending`, so public production creation returns `LicenseReviewPending` without observing the supplied path.
- Required Approved injected provenance plus explicit third-party and native-code policy permission before construction.
- Acquired independent video and audio verified-runtime leases and ABI gates and rejected shared identities.
- Constructed exact FLV binding, complete H.264／AAC native session, one-frame decode, fresh bounded scheduler, generation timing, and 0.0.9q delivery in fixed order.
- Returned only one complete caller-owned `PlaybackProductionDecoderGeneration` and exposed finite transfer, one-item delivery, reset, and stop operations.
- Kept source sessions and application-owned Windows output boundaries as non-owning borrows.
- Unwound only untransferred partial ownership in reverse order with reference-deduplicated cleanup.
- Serialized creation and made factory disposal cancel and await active construction without owning completed products.
- Added fixed policy, runtime, ABI, ownership, native-session, generation, cancellation, disposal, and fault results.
- Added ten focused cross-platform tests plus ADR-0129 and PCN-0137.
- Isolated one intentional CA1822 case and fixture-only call-log assertions through temporary status-encoding CircleCI jobs, then restored the routine configuration byte-for-byte.
- Selected 0.0.9t Production Generation Ingress and Replacement Composition Review.
- Kept candidate approval, normal backend registration, production PCP ingress composition, Windows activation, FFmpeg distribution, continuous pumping, and audible／visible playback unchanged.

### 0.0.9r - Production Decoder Generation Factory and Activation Review

- Reviewed production ownership from policy and verified runtime acquisition through independent video／audio ABI gate／context chains into the complete 0.0.9q generation.
- Required license／backend policy evaluation before runtime-path observation or native work.
- Preserved the pinned FFmpeg candidate as `LicenseReview.Pending`, so current production creation remains fail closed.
- Required two independently verified runtime leases and two distinct ABI gate leases because each context disposal consumes its own parent gate and runtime chain.
- Kept runtime acquisition explicit, host-installed, fixed-drive, and pinned-inventory-only without `PATH`, registry, network search, download, extraction, or bundling.
- Defined complete-product-only construction from exact FLV parameter binding through native session, decode boundary, fresh bounded scheduler, generation timing, and 0.0.9q delivery.
- Kept the FLV source session and Windows activation／binding／stream environment as non-owning application borrows.
- Defined reverse partial-failure cleanup and stop-old-before-create-new discontinuity replacement without overlap, revival, or rollback.
- Kept `PlaybackDecoderBackendFactory`, candidate provenance, normal backend registration, and distribution decisions unchanged.
- Selected 0.0.9s Policy-gated Production Decoder Generation Factory Boundary.
- Added ADR-0128 and PCN-0136 without changing runtime source or tests.
- Kept backend activation, Windows device activation, runtime distribution, continuous pumping, and audible／visible playback unavailable.

### 0.0.9q - Finite Generation-matched Scheduler Delivery and Output Lifecycle Boundary

- Added `PlaybackGenerationMatchedSchedulerDeliveryBoundary` as one finite owner of the exact source-bound decode generation, 0.0.9o scheduler ingress, and one-item delivery lifecycle.
- Required exact source, decoder／discontinuity generation, scheduler sequence, media role, frame liveness, and matching context role before output.
- Kept the source-bound native session as the sole video／audio context owner and selected only the matching H.264 or AAC context.
- Added one caller-driven reserve→reset／timing→adapt／convert→result-aware output→completion operation without a resident loop or hidden retry.
- Added typed scheduler reservation completion for retained, delivered, and dropped ownership while preserving the existing boolean API.
- Counted deliberate late-video removal as a scheduler drop rather than a delivery.
- Retained frames through `WouldBlock`, initialization／binding／resize requirements, audio recovery, cancellation, clock-not-started, and device loss.
- Added one-shot replacement-generation `DecoderReset` admission after successful timing／output reset and preserved the marker on cancellation or failure.
- Prevented decoded-completion detach while a scheduler delivery remained active.
- Added asynchronous stop that cancels and awaits active delivery before scheduler, decode-boundary, and native-session cleanup.
- Added ten focused cross-platform tests plus ADR-0127 and PCN-0135.
- Passed the initial routine CircleCI restore, Release build with analyzers, full test suite, and format verification without diagnostic configuration changes.
- Kept production generation factory, ABI-gate acquisition, backend activation, Windows device lifecycle, FFmpeg distribution, continuous pumping, and audible／visible playback unchanged.

### 0.0.9p - Generation-matched Scheduler Delivery and Output Lifecycle Review

- Reviewed the ownership connection from the 0.0.9o scheduler ingress through reservation, exact FFmpeg context selection, managed conversion, timing, and output lifecycle.
- Rejected production use of destructive `BoundedPlaybackScheduler.RunOneAsync` because it cannot retain a frame through output backpressure and accepts an unbound caller context.
- Selected scheduler reservation as the sole production delivery foundation and kept each operation finite and caller-driven.
- Required one generation owner with singular source-bound native-session ownership rather than a second decoder-context lifetime owner.
- Required video frames to use only their generation's video context and audio frames to use only the matching audio context.
- Selected an injected result-aware canonical output seam so `WouldBlock`, initialization／binding／resize, device loss, cancellation, and recovery can retain ownership.
- Required explicit delivered／dropped／retained reservation completion so deliberate video drop is not counted as delivery.
- Defined one-shot acknowledgement of the first replacement-generation `DecoderReset` after successful reset admission, avoiding an immutable-reservation reset loop.
- Required asynchronous shutdown: close admission, cancel and await active delivery, release active completion and scheduler ownership, then dispose native contexts.
- Kept Windows activation／binding／stream initialization and device-wide COM／OS resources externally owned.
- Selected 0.0.9q Finite Generation-matched Scheduler Delivery and Output Lifecycle Boundary.
- Added ADR-0126 and PCN-0134 without changing runtime source or tests.
- Kept production generation factory, backend activation, FFmpeg distribution, continuous pumping, and audible／visible playback unchanged.

### 0.0.9o - One-shot Decoded Completion Transfer and Scheduler Ingress Boundary

- Added explicit attached／detached／disposed ownership states to `PlaybackSourceBoundDecodedFrameCompletion`.
- Added generation-controlled one-shot detach authorized by the exact active `PlaybackSourceBoundOneFrameDecodeBoundary`.
- Revalidated source, decoder／discontinuity generation, media, stream, stream-local sequence, completion mode, managed bytes, frame liveness, and frame role before detach.
- Ensured completion disposal after detach releases decode backpressure without disposing the transferred managed frame.
- Added `PlaybackDecodedCompletionSchedulerIngressBoundary` as one generation-scoped owner of the existing bounded scheduler and matching decoder-context lifetime.
- Kept independent video／audio source sequences as identity and assigned a separate monotonic cross-media scheduler sequence.
- Committed source-admission sequence, scheduler sequence, accepted-transfer count, and first-frame `DecoderReset` only after accepted scheduler ownership.
- Preserved completion ownership, sequence state, and reset state on cancellation before detach.
- Made invalid completion, repeated transfer, sequence exhaustion, rejected／undefined enqueue, and enqueue exception terminal without rollback, frame reconstruction, or retry.
- Disposed scheduler frames before matching decoder-context lifetime on terminal cleanup and normal disposal.
- Added eleven focused cross-platform tests plus ADR-0125 and PCN-0133.
- Used temporary compiler／analyzer status-encoding CircleCI workflows to isolate CA1512 and nullable test diagnostics before restoring the routine merge gate.
- Kept direct awaited settlement, generation-matched scheduler delivery, production generation factory, backend activation, Windows output lifecycle, FFmpeg distribution, and audible／visible playback unchanged.

### 0.0.9m - Finite Awaited Decoded Completion Composition Boundary

- Added `PlaybackPcpFlvAwaitedDecodeCompositionBoundary` as a serialized finite composition from callback-scoped PCP payload through the real bounded FLV parser and the 0.0.9k source-bound decode generation.
- Kept the existing 0.0.9i boundary unchanged for compatibility and comparison while making the 0.0.9k boundary the new generation's sole packet-submission and native-receive authority.
- Advanced callback payload suffix only by exact parser `ConsumedByteCount` and completed each source-owned parser event before resuming the same PCP callback.
- Held retained packet／flush relief inside the open callback or explicit-finalization operation.
- Performed one native receive, one awaited exact-completion settlement, completion release, and one retry-only transition per relief cycle.
- Blocked parser suffix, later PCP input, and retained retry while decoded settlement remained incomplete.
- Bounded repeated `EAGAIN` by shared decode-operation, decoded-frame-count, and managed-byte budgets.
- Added one injected generation-scoped settlement seam and accepted only `Consumed` in this milestone; transferable scheduler ownership remains deferred.
- Revalidated exact source, decoder／discontinuity generation, media, stream, decoded sequence, managed bytes, frame liveness, and media role before and after settlement.
- Added callback-external one-frame ordinary receive and accepted-flush drain operations.
- Kept encoded finalization, independent video／audio flush acceptance, per-stream decoder EOF, and full decoder drain as distinct states.
- Replaced the old settlement and decode generation before parser discontinuity reset and new generation creation.
- Added eleven focused cross-platform tests for callback blocking, retry ordering, repeated relief, settlement rejection, cumulative budgets, ordinary receive, explicit finalization, independent drain, cancellation, discontinuity, and identity failure.
- Added ADR-0123 and PCN-0131 and documented a narrow CA2250 rationale for subordinate-enum／linked-stop cancellation translation after cleanup.
- Used temporary compiler/analyzer status-encoding workflows to isolate CS1026 and CA2250, then restored the routine CircleCI configuration byte-for-byte.
- Kept scheduler ownership transfer, production generation factory, backend activation, Windows output lifecycle, FFmpeg distribution, and audible／visible playback unchanged.

### 0.0.9l - Awaited Decoded Completion Composition Review

- Reviewed the ownership connection from the awaited PCP／FLV composition to the source-bound one-frame receive and EOS-drain boundary.
- Selected one future generation owner containing the 0.0.9k decode boundary rather than a parallel native session and submission state machine.
- Defined an injected awaited decoded-completion settlement seam where incomplete async work is the backpressure signal and completed outcomes are consumed, transferred, rejected, cancelled, or faulted.
- Required retained packet／flush relief to keep the PCP callback or explicit-finalization operation open until one decoded completion is settled and one retry-only transition completes.
- Kept ordinary post-accept receive and accepted-flush drain outside the packet callback as serialized one-frame caller operations.
- Separated encoded-input finalization, independent video／audio decoder EOF, and remaining downstream scheduler ownership.
- Reused `BoundedPlaybackScheduler` as the only decoded queue and rejected another composition-owned queue or resident pump.
- Identified that 0.0.9k stream-local video／audio sequences cannot directly satisfy the scheduler's single cross-media sequence; selected a separate checked generation-wide downstream-admission sequence committed only after accepted transfer.
- Required one-shot managed-frame detachment for scheduler ownership and generation-matched context lifetime for queued frames.
- Required explicit discontinuity to dispose active decoded ownership and old scheduler frames before the old decode boundary and native session.
- Defined shared relief frame／byte budgets, cancellation commit rules, terminal failure behavior, and decoded-completion／scheduler／decode-boundary／parser／factory cleanup order.
- Selected 0.0.9m Finite Awaited Decoded Completion Composition Boundary.
- Added ADR-0122 and PCN-0130 without changing runtime source or tests.

### 0.0.9k - Source-bound One-frame Receive and EOS Drain Boundary

- Added one exact source-bound decode generation that owns an opened native decoder session and one dedicated finite packet-submission state machine.
- Replaced state-only relief observation with a staged progress adapter so a successful native receive is copied exactly once before any retained retry.
- Performed at most one callback-scoped native receive and managed-frame copy per public receive operation.
- Added one source／generation／stream／media-bound decoded completion with independent checked video／audio sequence and managed-byte ownership.
- Allowed only one active decoded completion across the generation and committed sequence only after completion construction and work-budget acceptance.
- Added explicit ordinary, pending-submission relief, and accepted-flush drain modes.
- Retried retained packet／flush ownership only after the produced relief completion was released, with repeated `EAGAIN` requiring another frame and another finite retry operation.
- Drained accepted video and audio flushes independently one frame per call until each decoder EOF.
- Treated ordinary `NeedMoreInput` as normal and relief／flush-drain `NeedMoreInput` as terminal no progress.
- Added per-frame managed-byte limits, reusable frame-count／byte budgets, cancellation handling, fixed snapshots, and decoded-frame-first cleanup.
- Added eighteen focused cross-platform tests plus ADR-0121 and PCN-0129.
- Isolated CA1068 with a file-scoped documented analyzer setting and corrected the test fixture to use the required final `EndOfStream` source result for the second per-stream EOS.
- Restored the routine CircleCI configuration after diagnostics and kept PCP composition wiring, scheduler delivery, production activation, FFmpeg distribution, and audible／visible playback unchanged.

### 0.0.9j - Bounded Decode Iteration and Per-stream EOS Drain Review

- Reviewed decoded output after the 0.0.9i source-bound packet／flush composition.
- Confirmed that the current production `Advance` uses a state-only receive that frees a successful native frame before its data can be preserved.
- Selected one future source-bound receive authority that performs exactly one native receive and copies exactly one managed frame in the same callback-scoped operation.
- Required one active decoded completion across the generation with exact source, generation, stream, media, decoded sequence, and discontinuity identity.
- Required retained packet／flush retry only after a produced frame has been accepted; frame backpressure keeps both decoded and encoded ownership pending.
- Separated ordinary post-accept receive from the awaited PCP callback and limited each caller-driven operation to one frame.
- Distinguished parser EOS, accepted null-packet flush, flush-accepted drain, and decoder EOF.
- Required independent video／audio drain to EOF and rejected flush acceptance alone as full decoded completion.
- Defined mode-specific `NeedMoreInput`: normal after ordinary packet drain, no-progress failure during retained-send relief or accepted-flush drain.
- Defined frame-count／byte budgets, cancellation rules, discontinuity generation replacement, and decoded-frame-first cleanup.
- Selected 0.0.9k Source-bound One-frame Receive and EOS Drain Boundary.
- Added ADR-0120 and PCN-0128 without changing runtime source or tests.

### 0.0.9i - Finite Awaited PCP-to-FLV Composition Boundary

- Added a serialized awaited `IPcpChannelPacketConsumer` in `Nova.Playback`.
- Kept PCP packet payload memory callback-scoped and advanced a local suffix offset only by exact parser `ConsumedByteCount`.
- Supported multiple FLV records in one callback and one split record through the existing bounded parser carry without another encoded queue.
- Created one exact parser-source-bound decoder generation after `StreamDescriptionAvailable` through an injected factory.
- Passed every source-owned packet or per-stream EOS completion through the finite one-event submission state machine before resuming callback suffix input.
- Bounded parser operations and decoder advances independently and failed closed on no progress, accounting contradiction, identity／sequence／generation mismatch, rejected submission, undefined state, and work-limit exhaustion.
- Added separate explicit `CompleteInputAsync` for video-then-audio EOS and never inferred EOS from empty input, transport closure, reconnect, stop, timeout, cancellation, or failure.
- Replaced the submission state machine and native session before explicit parser discontinuity reset.
- Exposed fixed enum state, bounded counters, fixed-message terminal failure, and idempotent ordered cleanup.
- Added thirteen focused cross-platform tests plus ADR-0119 and PCN-0127.
- Kept production ABI-gate acquisition, backend activation, decoded-frame drain／ownership, scheduler ingress, output wiring, runtime distribution, and audible／visible playback unchanged.

### 0.0.9h - PCP Chunk-to-FLV Parser Composition Review

- Reviewed the awaited PCP packet callback through the 0.0.9g parser facade and the 0.0.9e submission state machine.
- Selected callback-held, zero-suffix-copy composition: keep `ConsumeAsync` incomplete until all payload bytes are consumed or copied into bounded parser carry.
- Required exact `ConsumedByteCount` offset accounting and completion-before-suffix processing.
- Required active parser／decoder completion and decoder `EAGAIN` to pause new parser and PCP input.
- Required bounded state-machine `Advance` transitions; no receive progress and operation-budget exhaustion fail closed.
- Required exact PCP channel／session／packet-sequence identity and exact parser-created source generation.
- Separated owner-invoked finalization because the packet-consumer interface has no EOS callback; transport close, reconnect, stop, timeout, cancellation, or failure are not EOS.
- Defined discontinuity generation replacement and ordered state-machine, native-session, parser, and generation cleanup.
- Selected 0.0.9i Finite Awaited PCP-to-FLV Composition Boundary.
- Added ADR-0118 and PCN-0126 without changing runtime source or tests.

### 0.0.9g - One-event Final EOS Sequencing

- Added a finite FLV parser facade for future production composition.
- Delegated ordinary parsing to the existing bounded FLV parser without changing packet payload, timing, or keyframe behavior.
- Latched final input only after all caller bytes were consumed and rejected incomplete final carry as truncated.
- Emitted video then audio EOS as separate source-owned completions through the exact parser-created source session.
- Preserved explicit per-stream EOS and emitted only the still-open stream during finalization.
- Tracked next per-stream sequence only from successfully constructed real parser completions.
- Preserved one-active-completion backpressure without a detached completion, copied EOS list, or second encoded queue.
- Accepted final signaling on an empty operation or alongside the last complete packet.
- Reset final, ended, and sequence state on explicit discontinuity.
- Composed both final EOS flushes through the 0.0.9e state machine.
- Added six focused tests plus ADR-0117 and PCN-0125.
- Kept PCP composition, native FFmpeg activation, decoded-frame ownership, output, backend activation, and runtime distribution unchanged.

### 0.0.9f - Controlled Retained FLV Fixture Validation

- Added one controlled in-memory FLV fixture using the real bounded FLV parser and exact parser-created source session.
- Passed real source-owned parser completions through the source-bound native-session identity and 0.0.9e state machine with injected native results.
- Preserved video／audio interleave, independent per-stream sequence, AVCC／AAC payload, DTS, signed composition-time PTS, and keyframe state.
- Verified packet and flush `EAGAIN` retention without a second encoded copy or hidden retry loop.
- Verified retained parser backpressure returns `ActiveCompletion` with zero consumed bytes.
- Verified each retained transition performs one receive and one retry only.
- Verified explicit video EOS followed by final audio EOS reaches independent flush acceptance.
- Recorded that an unpartitioned final completion containing both EOS events is rejected fail closed before native submission.
- Selected 0.0.9g One-event Final EOS Sequencing before PCP composition.
- Added two focused tests plus ADR-0116 and PCN-0124 without changing parser runtime behavior or production activation.

### 0.0.9e - Finite One-event Packet Submission State Machine

- Added one finite state machine over exactly one 0.0.9d packet or per-stream EOS completion.
- Split work into one initial `Begin` send／flush and one bounded `Advance` receive-plus-optional-retry transition.
- Performed at most one frame receive and at most one retained packet／flush retry per advance.
- Retried only after explicit frame progress and surfaced no-progress without a hidden loop.
- Retained the original native packet／flush completion and parser backpressure across repeated `EAGAIN`.
- Committed per-stream sequence and flush state only after native acceptance.
- Tracked independent video／audio flush acceptance and decoder EOF and rejected later input for ended streams.
- Rejected source mismatch, non-increasing sequence, concurrent input, invalid stream identity, and post-flush packets before native work.
- Added cancellation, terminal-failure, and idempotent packet-first cleanup without owning source or decoder sessions.
- Added fifteen focused tests plus ADR-0115 and PCN-0123.
- Kept resident decode, decoded-frame delivery, second encoded queues, PCP composition, backend activation, FFmpeg distribution, and playback unchanged.

### 0.0.9d - Metadata-aware Native Packet and Flush Boundary

- Added one finite source-bound operation for exactly one bounded FLV packet or per-stream EOS completion.
- Required exact source identity, stream ID, media kind, codec role, keyframe role, and snapshotted time base before native work.
- Mapped integral PTS, DTS, duration, stream index, time base, and keyframe state without implicit rescaling.
- Mapped unknown PTS/DTS to `AV_NOPTS_VALUE` and unknown duration to `0`; rejected negative duration and non-integral conversion.
- Allocated FFmpeg-owned packet and padded payload storage and wrote only the reviewed FFmpeg 8 x64 packet metadata prefix.
- Issued at most one packet send or null flush per operation.
- Retained native packet plus active parser completion on packet `EAGAIN`, and retained per-stream EOS completion on flush `EAGAIN`.
- Preserved source finite-read backpressure and packet-before-parser idempotent cleanup.
- Accepted first-stream EOS from `PacketsAvailable` and final-stream EOS from `EndOfStream` completions.
- Added fourteen focused tests plus ADR-0114 and PCN-0122.
- Kept receive iteration, hidden retry, second encoded queue, PCP composition, backend activation, FFmpeg distribution, and output unchanged.

### 0.0.9c - AVPacket Metadata and Flush ABI Review

- Approved one future metadata-aware native packet／flush boundary only for the exact verified FFmpeg 8 runtime identity.
- Rejected direct field writes under a major-version-only or arbitrary-runtime assumption.
- Required `av_packet_alloc`／`av_packet_free` ownership and prohibited managed allocation based on `sizeof(AVPacket)`.
- Approved only the reviewed PTS, DTS, data, size, stream index, key flag, duration, and time-base prefix needed for decoding.
- Mapped unknown timestamps to `AV_NOPTS_VALUE`, unknown duration to `0`, and parser keyframe state to `AV_PKT_FLAG_KEY`.
- Required exact source-session stream and time-base matching without implicit rescaling.
- Selected `avcodec_send_packet(context, NULL)` as the only EOS flush representation and kept `avcodec_flush_buffers` outside EOS drain.
- Required one finite send attempt, explicit `EAGAIN` retention, no second encoded queue, and no hidden retry or drain loop.
- Added ADR-0113 and PCN-0121. Runtime source, native ABI implementation, packet submission, flush execution, drain, PCP composition, activation, distribution, and output remain unchanged.
- Audited ADR／PCN numbering and recorded ADR-0096 and PCN-0104 as permanent numbering-error gaps, following the ADR-0082 precedent without retroactive reuse.

### 0.0.9b - Source-bound Opened Native Decoder Session

- Added one finite factory from a live FLV parameter binding plus distinct video/audio ABI gate leases into one opened H.264/AAC decoder session.
- Snapshotted exact source-session identity, stream IDs, and time bases before native work.
- Completed video context creation, parameter transfer, and codec open before beginning audio.
- Consumed the binding and both gates on every result and returned no partial configured or opened success state.
- Failed closed on invalid configuration, source disposal, context rejection, parameter-transfer rejection, codec-open rejection, and faults.
- Added audio-before-video idempotent cleanup without owning the parser or source demux session.
- Added six focused injected-operation tests and fixed one CS9124 test-only compiler issue.
- Added ADR-0112 and PCN-0120.
- Kept AVPacket metadata writes, packet submission, flush, drain, PCP composition, backend activation, and output unchanged.

### 0.0.9a - Timestamp-aware Packet Submission Architecture Review

- Determined that source-bound opened native contexts must exist before packet submission.
- Rejected the old diagnostic native decoder lease for real FLV composition because it lacks source identity, stream IDs, or time bases.
- Selected 0.0.9b for source-bound native session ownership and 0.0.9c for exact AVPacket metadata and flush ABI review.
- Required active parser-completion retention under decoder backpressure, explicit null-packet EOS flush, bounded drain, no second encoded queue, and no hidden retry loop.
- Added ADR-0111 and PCN-0119.

### 0.0.8z - FLV Session Decoder-Parameter Binding

- Added a pure managed adapter from one exact live FLV session to independently owned padded H.264/AAC codec-parameter leases.
- Preserved source identity, stream IDs, and `1/1000` time bases without native calls or packet consumption.
- Added ADR-0110 and PCN-0118.

### 0.0.8x-0.0.8y - Bounded FLV Parser and Composition Review

- Added the pure managed bounded FLV v1 parser with owned packet/EOS completions, strict AVC/AAC bootstrap, timestamps, discontinuity, and one-active-completion backpressure.
- Reviewed the parser-to-decoder seam and rejected metadata-losing reuse of the paired diagnostic packet transaction.
- Added ADR-0108/0109 and PCN-0116/0117.

### Earlier active playback milestones

0.0.8v and earlier active integration entries remain available in repository history and their ADR/PCN records.
