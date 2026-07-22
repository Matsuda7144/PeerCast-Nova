# PeerCast Nova Navigation

## Phase 5 controlled Windows video revalidation runbook

A dedicated Windows PowerShell helper now fixes the retained fixture identity, requires an exact clean `main` commit, keeps the approved FFmpeg runtime, fixture, and canonical record outside the repository, builds the dedicated runner, and executes exactly one physical `video` run. It does not start audio or paired-record verification. The operator still records `Video visible: Yes／No` separately from deterministic evidence.

- `tools/Run-ControlledWindowsPhysicalVideo.ps1`
- `docs/runbooks/0.0.10d-controlled-windows-video-revalidation.md`

## Phase 5 production timing presentation origin

The production monotonic timing path now anchors the first valid decoded presentation timestamp to the first playback-position read. Subsequent video and audio decisions preserve the original shared source timeline as relative offsets from that anchor. The 250 ms maximum wait and all early and late tolerances remain unchanged; raw timestamps are not rewritten, zero is not synthesized, and no sleep, retry, queue, or fixture change is added. Transactional timing reset clears both the monotonic position origin and the presentation anchor.

- `docs/adr/ADR-0145-production-timing-presentation-origin.md`
- `docs/reviews/PCN-0153-PRODUCTION-TIMING-PRESENTATION-ORIGIN.md`
- `src/Nova.Playback/PlaybackAvTimingDecisionBoundary.cs`

## Phase 5 decoded timestamp propagation

The bounded FLV native decoder session now configures the exact parser-provided stream time base through the public FFmpeg `av_opt_set_q` API and the `pkt_timebase` option. Configuration occurs after parameter transfer and before codec open for video and audio independently. When a decoded frame reports an exactly unset `0/positive` raw time base, metadata conversion uses the context lease's retained configured value; a valid raw frame time base remains authoritative and malformed raw values remain fail-closed. No timestamp is synthesized, no retry or fallback path is added, and physical video/audio success remains unclaimed.

- `docs/adr/ADR-0144-explicit-decoder-packet-time-base-propagation.md`
- `docs/reviews/PCN-0152-DECODED-TIMESTAMP-PROPAGATION.md`
- `src/Nova.Playback/PlaybackDecoderFfmpegPacketTimeBaseConfiguration.cs`

## 現在地

**0.0.10d — Controlled Physical Windows Video／Audio Output Validation — Phase 5 Independent Run Record Admission In Progress**

Phase 4の明示physical-run compositionに続き、videoとaudioを独立実行した結果を固定schemaのsanitized recordとして出力し、2記録をfail-closedで受理する境界を追加しました。この環境では実機D3D11／WASAPI runを実行していないため、0.0.10dのphysical validation完了はまだ記録していません。

- 各physical runはschema version 1のcanonical bounded text recordをstdoutへ出力
- recordはmilestone、kind、runtime build ID、fixture SHA-256、固定result、有限counter、terminal state、borrowed-owner preservationだけを保持
- runtime path、fixture path、device／endpoint identity、HWND、thread ID、pointer、handle、packet／decoded bytesを保持しない
- parserは固定header／footer／field順序を要求し、missing／extra／reordered／trailing／oversized contentを拒否
- video recordとaudio recordは別々に1件だけ受理
- 両recordは同一approved runtime buildと同一controlled fixture digestを必須化
- `Completed`＋`Accepted` evidence、positive progress、exactly one delivery、zero retained `WouldBlock`を必須化
- runner=`Stopped`、lifecycle=`Disposed`、borrowed window／dispatcher preserved、external publication=falseを必須化
- duplicate kind、identity mismatch、evidence contradiction、cleanup contradictionはterminal reject
- 片方だけのrecordではsetを完了せず、他方を待つ
- 専用verify modeはbounded external record filesだけを読み、runner／runtime／fixture／physical outputを開始しない
- verify reportはrecord pathを表示せず、`Physical-Runs-Executed-By-This-Command: False`を明示
- operatorによるvisible／audible observationはdeterministic admissionとは別のsupporting noteとして扱う
- Linux CIはsynthetic sanitized recordsだけを使い、physical outputを主張しない

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終headでrestore、Release build with analyzers、full solution tests、format verificationを通してからmergeします。

## 最新の入口

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
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsPhysicalRunRecordTests.cs`
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsIndependentRunSetCommandTests.cs`
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

**0.0.10d Phase 5 — Execute Controlled Physical Video Revalidation**

明示Windows 11 runnerでexact approved runtimeとretained controlled fixtureを使用し、修正後mainからvideo runだけを実行してcanonical recordをrepository外へ保存します。deterministic evidenceが`Completed`／`Accepted`となり、operatorの`Video visible: Yes／No`が記録されるまで、audio runとpaired-record verifyを開始しません。

## 未完了

- controlled physical Windows video output validation
- controlled physical Windows audio output validation
- combined controlled audible／visible playback validation
- GUI legal／about surface
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- GUI、installer、recorder
