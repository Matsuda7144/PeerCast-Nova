# PeerCast Nova Navigation

## Phase 5 controlled known-pattern Windows output probe

The physical video run completed with accepted deterministic evidence and `VideoAccepted` canonical preflight, but the three-second operator observation showed only a black window, so `Video visible: No` is recorded. A diagnostic-only probe now generates one known opaque non-black 320×240 BGRA four-quadrant frame and sends it exactly once through the same controlled Windows activation, binding, stream, buffer, D3D11 Present, window, dispatcher, and cleanup path without FFmpeg, media, decode, conversion, scheduler, timing, or audio output.

- `docs/adr/ADR-0150-controlled-known-pattern-windows-output-probe.md`
- `docs/reviews/PCN-0158-CONTROLLED-KNOWN-PATTERN-WINDOWS-OUTPUT-PROBE.md`
- `docs/runbooks/0.0.10d-controlled-windows-known-pattern-probe.md`
- `src/Nova.Cli/PlaybackControlledWindowsKnownPatternProbe.cs`
- `src/Nova.Cli/PlaybackControlledWindowsKnownPatternProbeCommand.cs`
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsKnownPatternProbeTests.cs`
- `tools/Run-ControlledWindowsKnownPatternProbe.ps1`

## Phase 5 video-gated controlled audio run

A dedicated Windows audio helper requires an independently `VideoAccepted` external record before native audio activation. It can then execute exactly one physical `audio` run, keep the accepted WASAPI buffer alive for a bounded one-second observation interval, write one external canonical audio record, and require `AudioAccepted` single-record preflight. Because recognizable video is not yet visible, audio and paired verification remain blocked.

- `docs/adr/ADR-0148-video-gated-controlled-audio-run.md`
- `docs/reviews/PCN-0156-VIDEO-GATED-CONTROLLED-AUDIO-RUN.md`
- `tools/Run-ControlledWindowsPhysicalAudio.ps1`
- `docs/runbooks/0.0.10d-controlled-windows-audio-validation.md`

## Phase 5 single physical run record preflight

A verify-only command reads one repository-external canonical physical record through the same bounded UTF-8 reader and submits it to the same independent run-set admission boundary used by paired verification. The dedicated video helper invokes this preflight after a successful physical exit and refuses to continue unless the record is independently `VideoAccepted`. The preflight starts no runner, runtime, fixture, decode, or physical output and never prints the record path.

- `docs/adr/ADR-0147-single-physical-run-record-preflight.md`
- `docs/reviews/PCN-0155-SINGLE-PHYSICAL-RUN-RECORD-PREFLIGHT.md`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunRecordPreflightCommand.cs`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunRecordFile.cs`
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsPhysicalRunRecordPreflightCommandTests.cs`

## Phase 5 bounded physical output observation hold

The controlled physical-run composition keeps an already delivered frame and its existing output lifecycle alive for one explicit bounded operator-observation interval before evidence capture and cleanup. The default remains zero; `--observation-hold-ms` accepts 0 through 30,000 ms, and the dedicated video helper requests three seconds. No decode, scheduler delivery, retry, resubmission, native output operation, timestamp change, or evidence mutation occurs during the hold.

- `docs/adr/ADR-0146-bounded-physical-output-observation-hold.md`
- `docs/reviews/PCN-0154-BOUNDED-PHYSICAL-OUTPUT-OBSERVATION-HOLD.md`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunComposition.cs`
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsPhysicalObservationHoldTests.cs`

## Phase 5 controlled Windows video revalidation runbook

The Windows PowerShell helper fixes the retained fixture identity, requires an exact clean `main` commit, keeps the approved FFmpeg runtime, fixture, and canonical record outside the repository, builds the dedicated runner, and executes exactly one physical `video` run. The completed run produced `Completed`, `Accepted`, and `VideoAccepted` deterministic results, but the independent operator observation was `Video visible: No` because only a black window was visible.

- `tools/Run-ControlledWindowsPhysicalVideo.ps1`
- `docs/runbooks/0.0.10d-controlled-windows-video-revalidation.md`

## Phase 5 production timing presentation origin

The production monotonic timing path anchors the first valid decoded presentation timestamp to the first playback-position read. Subsequent video and audio decisions preserve the original shared source timeline as relative offsets from that anchor. The 250 ms maximum wait and all early and late tolerances remain unchanged; raw timestamps are not rewritten, zero is not synthesized, and no sleep, retry, queue, or fixture change is added. Transactional timing reset clears both the monotonic position origin and the presentation anchor.

- `docs/adr/ADR-0145-production-timing-presentation-origin.md`
- `docs/reviews/PCN-0153-PRODUCTION-TIMING-PRESENTATION-ORIGIN.md`
- `src/Nova.Playback/PlaybackAvTimingDecisionBoundary.cs`

## Phase 5 decoded timestamp propagation

The bounded FLV native decoder session configures the exact parser-provided stream time base through the public FFmpeg `av_opt_set_q` API and the `pkt_timebase` option. Configuration occurs after parameter transfer and before codec open for video and audio independently. When a decoded frame reports an exactly unset `0/positive` raw time base, metadata conversion uses the context lease's retained configured value; a valid raw frame time base remains authoritative and malformed raw values remain fail-closed. No timestamp is synthesized, no retry or fallback path is added.

- `docs/adr/ADR-0144-explicit-decoder-packet-time-base-propagation.md`
- `docs/reviews/PCN-0152-DECODED-TIMESTAMP-PROPAGATION.md`
- `src/Nova.Playback/PlaybackDecoderFfmpegPacketTimeBaseConfiguration.cs`

## 現在地

**0.0.10d — Controlled Physical Windows Video／Audio Output Validation — Phase 5 Black-output Diagnosis In Progress**

Windows 11でcontrolled physical video runを2回実行し、最新runはmain `8c87337bfd6996fa5c018eda512ec143c83be6e4`、helper exit 0、`Completed`、`Accepted`、`VideoAccepted`、exactly one delivery、zero retained `WouldBlock`、runner stopped、lifecycle disposed、borrowed owners preserved、external publication falseを記録しました。一方、operator observationは認識可能なtest patternではなく黒画面だけだったため、`Video visible: No`です。

- deterministic D3D11 call accountingとcanonical video admissionは成功
- recognizable visible videoは失敗
- deterministic evidenceとoperator observationは独立して保持
- known non-black BGRA probeでdecode／conversion側とWindows output側を分離
- probeはFFmpeg、fixture、parser、decoder、scheduler、timing、audio writeを使用しない
- audio runとpaired verificationは黒画面境界が解決するまで開始しない
- runtime path、fixture path、record path、device／endpoint identity、HWND、thread ID、pointer、handle、pixel payloadをrepositoryへ保持しない
- Linux CIはsynthetic／injected validationだけを使い、physical visibilityを主張しない

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終headでrestore、Release build with analyzers、full solution tests、format verificationを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0150-controlled-known-pattern-windows-output-probe.md`
- `docs/reviews/PCN-0158-CONTROLLED-KNOWN-PATTERN-WINDOWS-OUTPUT-PROBE.md`
- `docs/runbooks/0.0.10d-controlled-windows-known-pattern-probe.md`
- `src/Nova.Cli/PlaybackControlledWindowsKnownPatternProbe.cs`
- `src/Nova.Cli/PlaybackControlledWindowsKnownPatternProbeCommand.cs`
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsKnownPatternProbeTests.cs`
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsKnownPatternProbeCommandTests.cs`
- `tools/Run-ControlledWindowsKnownPatternProbe.ps1`
- `docs/adr/ADR-0148-video-gated-controlled-audio-run.md`
- `docs/reviews/PCN-0156-VIDEO-GATED-CONTROLLED-AUDIO-RUN.md`
- `tools/Run-ControlledWindowsPhysicalAudio.ps1`
- `docs/runbooks/0.0.10d-controlled-windows-audio-validation.md`
- `docs/adr/ADR-0147-single-physical-run-record-preflight.md`
- `docs/reviews/PCN-0155-SINGLE-PHYSICAL-RUN-RECORD-PREFLIGHT.md`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunRecordPreflightCommand.cs`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunRecordFile.cs`
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsPhysicalRunRecordPreflightCommandTests.cs`
- `docs/adr/ADR-0146-bounded-physical-output-observation-hold.md`
- `docs/reviews/PCN-0154-BOUNDED-PHYSICAL-OUTPUT-OBSERVATION-HOLD.md`
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsPhysicalObservationHoldTests.cs`
- `tools/Run-ControlledWindowsPhysicalVideo.ps1`
- `docs/runbooks/0.0.10d-controlled-windows-video-revalidation.md`
- `docs/adr/ADR-0145-production-timing-presentation-origin.md`
- `docs/reviews/PCN-0153-PRODUCTION-TIMING-PRESENTATION-ORIGIN.md`
- `src/Nova.Playback/PlaybackAvTimingDecisionBoundary.cs`
- `tests/Nova.Playback.Tests/PlaybackAvTimingPresentationOriginTests.cs`
- `docs/adr/ADR-0143-independent-physical-run-record-admission.md`
- `docs/reviews/PCN-0151-INDEPENDENT-PHYSICAL-RUN-RECORD-ADMISSION.md`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunRecord.cs`
- `src/Nova.Cli/PlaybackControlledWindowsIndependentRunSetBoundary.cs`
- `src/Nova.Cli/PlaybackControlledWindowsIndependentRunSetCommand.cs`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunCommand.cs`
- `src/Nova.ControlledWindowsRunner/Program.cs`
- `docs/adr/ADR-0142-explicit-controlled-windows-physical-run-composition.md`
- `docs/reviews/PCN-0150-EXPLICIT-CONTROLLED-WINDOWS-PHYSICAL-RUN-COMPOSITION.md`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunComposition.cs`
- `src/Nova.Cli/SystemPlaybackControlledWindowsPhysicalRunOperations.cs`
- `src/Nova.Network/Pcp/PcpControlledFlvFixtureReplayBoundary.cs`
- `docs/adr/ADR-0141-application-owned-windows-physical-runner-harness.md`
- `docs/reviews/PCN-0149-APPLICATION-OWNED-WINDOWS-PHYSICAL-RUNNER-HARNESS.md`
- `src/Nova.Cli/PlaybackWindowsPhysicalRunnerHost.cs`
- `src/Nova.Cli/SystemPlaybackWindowsPhysicalRunnerNative.cs`
- `docs/adr/ADR-0140-system-native-windows-output-evidence-capture.md`
- `docs/reviews/PCN-0148-SYSTEM-NATIVE-WINDOWS-OUTPUT-EVIDENCE-CAPTURE.md`
- `src/Nova.Playback/PlaybackWindowsOutputNativeEvidenceCapture.cs`
- `docs/adr/ADR-0139-controlled-physical-windows-output-evidence-admission-boundary.md`
- `docs/reviews/PCN-0147-CONTROLLED-PHYSICAL-WINDOWS-OUTPUT-EVIDENCE-ADMISSION.md`
- `src/Nova.Playback/PlaybackControlledWindowsOutputEvidenceBoundary.cs`
- `docs/adr/ADR-0138-controlled-windows-video-audio-output-validation-review.md`
- `docs/reviews/PCN-0146-CONTROLLED-WINDOWS-VIDEO-AUDIO-OUTPUT-VALIDATION-REVIEW.md`
- `src/Nova.Playback/PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary.cs`

## 進捗記録の規律

実装・設計変更だけでなく、バグ修正にも同じ進捗記録の規律を適用します。

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.10d Phase 5 — Execute Controlled Known-pattern Windows Output Probe**

修正後mainからknown-pattern helperを1回だけ実行し、canonical probe recordをrepository外へ保存します。operatorは赤／緑／青／白の4分割が認識できたかを`Known pattern visible: Yes／No`で記録します。結果が確定するまでaudio runとpaired-record verifyを開始しません。

## 未完了

- controlled known-pattern Windows output diagnosis
- recognizable controlled physical Windows video output validation
- controlled physical Windows audio output validation
- combined controlled audible／visible playback validation
- GUI legal／about surface
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- GUI、installer、recorder
