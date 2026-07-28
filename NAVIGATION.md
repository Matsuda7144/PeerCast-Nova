# PeerCast Nova Navigation

## Phase 5 corrected physical video validation

The corrected physical video run completed once from clean main `cd7442e18d18acfe2faf43470ef1c97f77ec513f` with the approved external runtime and fixed retained fixture. The canonical record reported `Completed／Accepted`, exactly one delivery, zero retained WouldBlock, stopped runner, disposed lifecycle, preserved borrowed owners, and no external publication. Verify-only single-record preflight returned `VideoAccepted`, and the operator recorded `Video visible: Yes` for recognizable fixture content.

- `tools/Run-ControlledWindowsPhysicalVideo.ps1`
- `docs/runbooks/0.0.10d-controlled-windows-video-revalidation.md`

## Phase 5 video-gated controlled audio run

The accepted visible video record now opens the separately gated audio run. The dedicated Windows helper first requires that exact external record to pass `VideoAccepted`, then executes one physical `audio` run, keeps the accepted WASAPI buffer alive for the default one-second observation interval, retains one external canonical audio record, and requires `AudioAccepted`. It performs no paired verification.

The first audio attempt on clean main
`36ff2d616965975f9d42879cfbf4e23ded99ace1` passed `VideoAccepted` but its
single delivery attempt ended `DeliveryRejected` with zero delivered, zero
retained WouldBlock, and no native audio evidence. Audio preflight and operator
observation were not run; WASAPI reachability remains unproven. The cause was the
generation scheduler's video-first initial reservation conflicting with the
audio-only controlled run while both media kinds were queued. The corrected
controlled lifecycle maps its requested media kind to the existing bounded
scheduler's first successful reservation. Normal production remains video-first
and `DropIncoming`; controlled capacity `16`, `64 MiB`, and `DropOldest` are
unchanged. Corrected physical audio and paired verification remain pending.

The first corrected-selection audio attempt on clean main
`e8b7fcac2f679eb5023ae8ab36571b86773d3950` passed the video gate but
produced zero child stdout bytes and no canonical audio record. The helper exited
`2` and left a zero-byte external audio-record file. Counters, states, and owner
preservation are unknown; audio preflight, audible observation, and paired
verification were not executed. No retry or fallback occurred. Because the old
helper lost the child exit code and stderr boundary, no WASAPI call or crash code
is established, and the result does not refute PR #112.

The controlled diagnostic boundary now promotes only complete canonical stdout
and otherwise writes a separate repository-external sanitized failure record.
The dedicated explicit flag reports only fixed native audio stage names; raw
stderr, paths, device identities, pointers, handles, HRESULTs, and media data are
not recorded. The next physical audio command is diagnostic only and remains
blocked from final acceptance and paired verification.

The first diagnostic invocation from main
`4968e40f801f72bb06a71007a04a47911b6ac377` built the Release runner but
failed inside the Windows PowerShell 5.1 helper before physical-child start.
Neither record was created; physical output, preflight, audible observation, and
paired verification were not run. The corrected boundary keeps the documented
`powershell.exe -NoProfile -ExecutionPolicy Bypass` host while moving child
process construction, concurrent byte capture, diagnostics, and atomic record
promotion into the `net10.0`
`--capture-controlled-windows-audio-child` command. PowerShell 7 is not required,
and this host correction adds no WASAPI or final acceptance evidence.

- `docs/adr/ADR-0148-video-gated-controlled-audio-run.md`
- `docs/reviews/PCN-0156-VIDEO-GATED-CONTROLLED-AUDIO-RUN.md`
- `src/Nova.Cli/PlaybackControlledWindowsAudioChildCaptureCommand.cs`
- `tools/Run-ControlledWindowsPhysicalAudio.ps1`
- `docs/runbooks/0.0.10d-controlled-windows-audio-validation.md`

The later diagnostic run on clean main
`e7e82b01a1eb323954b8ef2375b025064a796a8d` passed the video gate and audio
preflight and produced a `Completed` / `Accepted` record with one delivery,
zero WouldBlock, stopped/disposed lifecycle, and preserved borrowed owners.
The operator recorded `Audio audible: No`: only a short noise-like sound was
heard, not recognizable fixture audio. Deterministic evidence remains valid,
recognizable physical audio remains incomplete, and paired verification is
blocked.

The approved four-frame run on clean main
`63f8be0cc5663ccda0f82bd5eee1c5a3dd3201d2` was executed exactly once and
must not be repeated. It passed the video gate but stopped with
`DeliveryRejected / None`, decode operations `3`, outer delivery attempts `2`,
delivered `1`, and retained WouldBlock `0`. The runner stopped and lifecycle
disposed. The operator recorded `Audio audible: No` after hearing only a
noise-like transient.

The later run on clean main `955ffcdad693391eed1535987c96589eb479741e`
was also executed exactly once and must not be repeated. It reproduced the same
`DeliveryRejected / None`, `3 / 2 / 1 / 0` decode, attempt, delivered, and
retained-WouldBlock counters, with `Audio audible: No`. Because PR #120 had
already added bounded handling for `RetainedWait`, the earlier claim that the
second outer result was necessarily `RetainedWait` is disproven. The exact fixed
lifecycle result was then isolated by the delivery-result diagnostic.
Paired verification remains blocked.

## Phase 5 controlled known-audio tone probe

The diagnostic-only probe generates one fixed managed 80 ms, 1,000 Hz,
48 kHz stereo float tone, submits exactly one production audio-buffer write,
requires one ReleaseBuffer and Start acknowledgement, holds without additional
output, and performs production-first cleanup. It bypasses FFmpeg, fixture,
parser, decoder, scheduler, timing, physical run records, and video Present.
The canonical path-free record distinguishes a recognizable beep from a
canonical PCM/WASAPI interpretation failure without claiming final physical
audio acceptance.

- `docs/adr/ADR-0149-controlled-known-audio-tone-probe.md`
- `docs/reviews/PCN-0157-CONTROLLED-KNOWN-AUDIO-TONE-PROBE.md`
- `src/Nova.Cli/PlaybackControlledWindowsKnownAudioToneProbe.cs`
- `tools/Run-ControlledWindowsKnownAudioToneProbe.ps1`

The separately approved physical probe on clean main
`2ca34405b8bdd81611aa2414092c91a95a8d6d51` completed with accepted evidence,
one acknowledged write and Start, stopped/disposed cleanup, preserved borrowed
owners, and operator observation `Known audio tone audible: Yes`. The 80 ms
1,000 Hz beep was recognizable. Canonical Float32 production WASAPI output is
therefore operational; paired verification remains blocked while fixture audio
assembly is isolated.

## Phase 5 controlled decoded-audio slice probe

This diagnostic reuses the approved runtime, retained fixture, existing FLV
demux/native AAC decode, and managed conversion. It copies the chronological
first 80 ms as three complete 1,024-sample frames plus 768 samples from the
fourth, yielding one private 3,840-frame / 30,720-byte canonical Float32 stereo
buffer. Only the complete aggregate reaches one production audio write. The
scheduler and normal one-frame composition are bypassed but unchanged.

- `docs/adr/ADR-0152-controlled-decoded-audio-slice-probe.md`
- `docs/reviews/PCN-0160-CONTROLLED-DECODED-AUDIO-SLICE-PROBE.md`
- `docs/runbooks/0.0.10d-controlled-decoded-audio-slice-probe.md`
- `src/Nova.Playback/PlaybackDecodedAudioSliceCollector.cs`
- `src/Nova.Cli/PlaybackControlledWindowsDecodedAudioSliceProbe.cs`
- `tools/Run-ControlledWindowsDecodedAudioSliceProbe.ps1`

## Phase 5 controlled audio retained timing resume

Controlled audio alone resumes `RetainedWait` after one upward-rounded
`1024 / 48000` AAC-frame cadence, with at most two resumes for one outer
delivery. The same scheduler item remains retained; no decode, transfer,
enqueue, replacement reservation, conversion, or native write occurs while
timing is early. `RetainedWouldBlock` and every other non-delivery result remain
terminal. Video and normal production timing are unchanged.

The post-merge physical reproduction at `955ffcdad693391eed1535987c96589eb479741e`
did not change the outer failure boundary. Therefore the timing-resume code
remains bounded and reviewed, but `RetainedWait` was not the actual second outer
terminal result.

- `docs/adr/ADR-0155-controlled-audio-retained-timing-resume.md`
- `docs/reviews/PCN-0163-CONTROLLED-AUDIO-RETAINED-TIMING-RESUME.md`
- `docs/runbooks/0.0.10d-controlled-audio-retained-timing-resume.md`
- `src/Nova.Cli/SystemPlaybackControlledWindowsPhysicalRunOperations.cs`
- `tests/Nova.Cli.Tests/SystemPlaybackControlledWindowsPhysicalRunOperationsTests.cs`

## Phase 5 controlled audio delivery-result diagnostic

The dedicated diagnostic reuses the unchanged physical-run composition through
an operations decorator. It records the exact fixed result returned by each
outer delivery call, in order, while delegating all work unchanged. The separate
record is bounded to the four-delivery audio target and explicitly states that it
is not an audio-acceptance record.

The approved diagnostic on clean main
`fe427fdc6094f55ea1a1f321217eff635febf898` executed exactly once. It recorded
`AudioDelivered` followed by `VideoDropped`, with decode operations `3`, outer
delivery attempts `2`, delivered `1`, retained WouldBlock `0`, and operator
observation `Audio audible: No`. No failure record was produced, and the command
must not be repeated.

- `docs/adr/ADR-0156-controlled-audio-delivery-result-diagnostic.md`
- `docs/reviews/PCN-0164-CONTROLLED-AUDIO-DELIVERY-RESULT-DIAGNOSTIC.md`
- `docs/runbooks/0.0.10d-controlled-audio-delivery-result-diagnostic.md`
- `src/Nova.Cli/PlaybackControlledWindowsDeliveryDiagnosticCommand.cs`
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsDeliveryDiagnosticCommandTests.cs`
- `tools/Run-ControlledWindowsPhysicalAudioDeliveryDiagnostic.ps1`

## Phase 5 controlled audio fixed-kind scheduler reservation

The exact `AudioDelivered` then `VideoDropped` sequence proves that the normal
A/V scheduler preference changed to video after the first audio delivery. The
controlled audio generation now opts into fixed-kind reservation: every
scheduler reservation searches audio only and never falls back to video.
Unmatched video remains scheduler-owned until ordinary bounded overflow, reset,
or generation cleanup.

The new scheduler option defaults to disabled. Controlled video and normal
production preserve initial preference, caller-driven A/V alternation, and
opposite-kind fallback. The implementation review performs no physical output.

- `docs/adr/ADR-0157-controlled-audio-fixed-kind-scheduler-reservation.md`
- `docs/reviews/PCN-0165-CONTROLLED-AUDIO-FIXED-KIND-SCHEDULER-RESERVATION.md`
- `docs/runbooks/0.0.10d-controlled-audio-fixed-kind-scheduler-reservation.md`
- `src/Nova.Playback/BoundedPlaybackScheduler.cs`
- `src/Nova.Cli/SystemPlaybackControlledWindowsPhysicalRunOperations.cs`
- `tests/Nova.Playback.Tests/BoundedPlaybackSchedulerRestrictedOutputTests.cs`

## Phase 5 corrected Windows output probes

The private `ID3D11DeviceContext` declarations omitted `OMSetBlendState` and `OMSetDepthStencilState`, shifting every later COM vtable call by two slots. The correction restored the official method order and native `void` signatures.

On main `5386637fdca7cab8dfd451491f99422930a67bfb`, both the diagnostic `CreateTexture2D+CopyResource` path and the production direct `UpdateSubresource` path completed with one Present acknowledgement, exact accepted bytes, complete cleanup, and visible red／green／blue／white quadrants.

- `docs/adr/ADR-0150-controlled-known-pattern-windows-output-probe.md`
- `docs/reviews/PCN-0158-CONTROLLED-KNOWN-PATTERN-WINDOWS-OUTPUT-PROBE.md`
- `docs/runbooks/0.0.10d-controlled-windows-known-pattern-probe.md`
- `tools/Run-ControlledWindowsKnownPatternProbe.ps1`
- `docs/adr/ADR-0151-controlled-d3d11-texture-copy-output-probe.md`
- `docs/reviews/PCN-0159-CONTROLLED-D3D11-TEXTURE-COPY-OUTPUT-PROBE.md`
- `docs/runbooks/0.0.10d-controlled-windows-texture-copy-probe.md`
- `tools/Run-ControlledWindowsTextureCopyProbe.ps1`
- `src/Nova.Playback/PlaybackWindowsOutputTextureCopyProbeBoundary.cs`
- `tests/Nova.Playback.Tests/PlaybackWindowsD3D11ComSignatureTests.cs`

## Phase 5 single physical run record preflight

A verify-only command reads one repository-external canonical physical record through the same bounded UTF-8 reader and submits it to the independent run-set admission boundary used by paired verification. It starts no runner, runtime, fixture, decode, or physical output and never prints the record path.

- `docs/adr/ADR-0147-single-physical-run-record-preflight.md`
- `docs/reviews/PCN-0155-SINGLE-PHYSICAL-RUN-RECORD-PREFLIGHT.md`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunRecordPreflightCommand.cs`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunRecordFile.cs`
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsPhysicalRunRecordPreflightCommandTests.cs`

## Phase 5 bounded physical output observation hold

The controlled physical-run composition keeps an already delivered item and its existing output lifecycle alive for one bounded operator-observation interval before evidence capture and cleanup. No decode, scheduler delivery, retry, resubmission, native output operation, timestamp change, or evidence mutation occurs during the hold.

- `docs/adr/ADR-0146-bounded-physical-output-observation-hold.md`
- `docs/reviews/PCN-0154-BOUNDED-PHYSICAL-OUTPUT-OBSERVATION-HOLD.md`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunComposition.cs`
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsPhysicalObservationHoldTests.cs`

## Phase 5 production timing and decoded timestamps

The bounded FLV decoder configures the exact parser stream time base through public FFmpeg APIs. The production timing path anchors the first valid decoded presentation timestamp to the first monotonic playback-position read and preserves later source-relative offsets. Raw timestamps are not rewritten, tolerances are unchanged, and no retry or fallback path is added.

- `docs/adr/ADR-0144-explicit-decoder-packet-time-base-propagation.md`
- `docs/reviews/PCN-0152-DECODED-TIMESTAMP-PROPAGATION.md`
- `src/Nova.Playback/PlaybackDecoderFfmpegPacketTimeBaseConfiguration.cs`
- `docs/adr/ADR-0145-production-timing-presentation-origin.md`
- `docs/reviews/PCN-0153-PRODUCTION-TIMING-PRESENTATION-ORIGIN.md`
- `src/Nova.Playback/PlaybackAvTimingDecisionBoundary.cs`

## 現在地

**0.0.10d — Controlled Physical Windows Video／Audio Output Validation — Phase 7 Separate Authorization Decision**

- authoritative state: `docs/reviews/PCN-0166-CONTROLLED-AUDIO-RENDER-AUTHORIZATION-STATE.md`, `docs/reviews/PCN-0167-MANAGED-AUDIO-FAILURE-BOUNDARY.md`, and Issue #129; documentation, CI, and readiness do not grant or select physical authorization;
- corrected decoded physical video: `Completed／Accepted`, `VideoAccepted`, visible `Yes`;
- known 80 ms tone and decoded fixture slice: accepted and recognizable;
- ordinary controlled fixture audio: consumed readiness-gated runs at `7fbeb8b0dabd9c6af5b2dc9d065d66fd05faf53d`, `77a947f98e7a9449523b49a39bc8b4e8d7ed430b`, and `774342a03c33713b1b7f70e2cb664397c04f8d04` each had operator observation `Audio audible: Yes` for a recognizable beep;
- the `7fbeb8...` run ended `DeliveryRejected / PreCleanupCheckpointRejected`; the `77a947f...` run ended `DeliveryRejected / PreCleanupTraceCardinalityRejected`;
- static source review identified a two-stage trace loss across production generation cleanup and later lifecycle production-owner cleanup;
- PR #201 merged as main `7e31627151f5a6dba7fb9e3f0b03cd08be3b14cf` and retains the immutable scheduler trace across both ownership boundaries without changing physical-output behavior;
- the prior cardinality rejection was an evidence-retention asymmetry, not proof of an extra native audio write;
- the later `774342a...` run ended child exit `1`, stdout／stderr bytes `0／3122`, final stage `ActivationCleanupCompleted`, controlled result `None`, and terminal rejection `None`; it retained version 5 sanitized failure SHA-256 `F8B8A0553A6DDF0935BFD2C075FFE32E541659E3A13C1275130E655363B5FAF5`;
- the historical private stderr classification for that run cannot be proven from the retained record and is not inferred;
- PR #203 merged as main `e265a2b47731214a2f26a0b4a4227721158490b2` and prospectively bounds unexpected direct managed-command failures as exact `Faulted` output with fixed private phase diagnostics and `ProjectionRejected` for snapshot／terminal-projection failures;
- none of the recognizable runs produced a canonical completed or terminal record or established physical completion or formal audio acceptance;
- retained contradictory ordinary／diagnostic results remain historical evidence and must not be rerun;
- prospective completed／terminal／child-failure／helper-failure retention and strict offline review are implemented;
- PR #192 through PR #203 added bounded run-result and terminal-rejection retention, exact audio stream-format validation, bounded readiness failure disclosure, direct readiness child capture, granular pre-cleanup rejection diagnostics, terminal trace retention, and managed command failure bounding;
- all recorded readiness and physical authorizations through `774342a...` are consumed; its `-retry1` readiness and outcome paths are not reusable; no current readiness capture or physical audio run is authorized;
- Issue #129 remains open and paired verification remains blocked;
- no additional concrete non-physical evidence gap is currently identified;
- approved runtime: `btbn.ffmpeg.20260721.win64.lgpl-shared`;
- fixture: `66,939` bytes, SHA-256 `32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15`;
- no runtime path, fixture path, record path, device／endpoint identity, HWND, thread ID, pointer, handle, HRESULT, raw process output, or payload is retained in the repository.

Any later physical audio run requires a separately selected exact `main`, final-head and post-merge CircleCI success, a newly captured and independently revalidated canonical readiness record, fresh repository-external outcome paths, a new explicit one-run authorization, and exactly one invocation through the readiness-required entrypoint. Readiness, a documentation-only merge, and the literal invocation guard are not authorization.

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終headでrestore、Release build with analyzers、full solution tests、format verificationを通してからmergeします。Physical Windows execution remains a separate explicit authorization boundary and is never implied by routine CI.

## 最新の入口

- `docs/reviews/PCN-0166-CONTROLLED-AUDIO-RENDER-AUTHORIZATION-STATE.md`
- `docs/reviews/PCN-0167-MANAGED-AUDIO-FAILURE-BOUNDARY.md`
- [Issue #129](https://github.com/Matsuda7144/PeerCast-Nova/issues/129)
- `docs/runbooks/0.0.10d-controlled-audio-render-readiness.md`
- `tools/Test-ControlledWindowsPhysicalAudioRenderEvidenceReadiness.ps1`
- `docs/runbooks/0.0.10d-controlled-audio-render-readiness-capture.md`
- `tools/Save-ControlledWindowsPhysicalAudioRenderEvidenceReadiness.ps1`
- `docs/runbooks/0.0.10d-controlled-audio-render-readiness-record-preflight.md`
- `docs/runbooks/0.0.10d-controlled-audio-render-authorization-entrypoint.md`
- `tools/Run-ControlledWindowsPhysicalAudioRenderEvidenceWithReadiness.ps1`
- `docs/runbooks/0.0.10d-controlled-audio-render-evidence.md`
