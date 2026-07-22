# PeerCast Nova Navigation

## 現在地

**0.0.10d — Controlled Physical Windows Video／Audio Output Validation — Phase 3 Runner Harness In Progress**

application-owned real window／message loop／owner-thread dispatcherを有限に提供するWindows 11 runner harnessを`Nova.Cli`へ追加しました。`Nova.Playback`はdispatcherとwindow tokenを借用するだけで、threadやwindowを所有しません。実機D3D11／WASAPI runはまだ実行していません。

- `Nova.Cli`が専用controlled-run thread、visible Win32 window、message loopを所有
- supported production pathはWindows 11以降だけを受理
- client size、title、pending上限をthread作成前にfail-closed検証
- application dispatcherはpending operation数を1〜64の固定上限で管理
- 上限超過は即時`Faulted`、hidden retry／delay／resubmitなし
- pre-cancelled operationは保持・実行せず`Cancelled`
- accepted operationはowner threadで直列実行
- owner-thread nested invocationはself-deadlock回避のためinline実行
- stop開始時にqueued operationを`Shutdown`でsettleし、新規受付を閉じる
- native window closeでもmessage loopを終了し、後続受付を閉じる
- snapshotはwindow token、owner managed thread ID、pending数、sanitized counterだけを保持
- runtime path、device identifier、media bytes、native output resourceをrunner snapshotへ含めない
- Linux CIはfake native pumpだけを使い、User32／D3D11／WASAPI実行を主張しない

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終headでrestore、Release build with analyzers、full solution tests、format verificationを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0141-application-owned-windows-physical-runner-harness.md`
- `docs/reviews/PCN-0149-APPLICATION-OWNED-WINDOWS-PHYSICAL-RUNNER-HARNESS.md`
- `src/Nova.Cli/PlaybackWindowsPhysicalRunnerHost.cs`
- `src/Nova.Cli/SystemPlaybackWindowsPhysicalRunnerNative.cs`
- `tests/Nova.Cli.Tests/PlaybackWindowsPhysicalRunnerHostTests.cs`
- `docs/adr/ADR-0140-system-native-windows-output-evidence-capture.md`
- `docs/reviews/PCN-0148-SYSTEM-NATIVE-WINDOWS-OUTPUT-EVIDENCE-CAPTURE.md`
- `src/Nova.Playback/PlaybackWindowsOutputNativeEvidenceCapture.cs`
- `tests/Nova.Playback.Tests/PlaybackWindowsOutputNativeEvidenceCaptureTests.cs`
- `docs/adr/ADR-0139-controlled-physical-windows-output-evidence-admission-boundary.md`
- `docs/reviews/PCN-0147-CONTROLLED-PHYSICAL-WINDOWS-OUTPUT-EVIDENCE-ADMISSION.md`
- `src/Nova.Playback/PlaybackControlledWindowsOutputEvidenceBoundary.cs`
- `tests/Nova.Playback.Tests/PlaybackControlledWindowsOutputEvidenceBoundaryTests.cs`
- `docs/adr/ADR-0138-controlled-windows-video-audio-output-validation-review.md`
- `docs/reviews/PCN-0146-CONTROLLED-WINDOWS-VIDEO-AUDIO-OUTPUT-VALIDATION-REVIEW.md`
- `src/Nova.Playback/PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary.cs`
- `src/Nova.Playback/PlaybackApprovedExternalRuntimeWindowsLifecycleOperations.cs`
- `src/Nova.Playback/PlaybackWindowsOutputActivationBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputResourceBindingBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputStreamInitializationBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputBufferProcessingBoundary.cs`
- `src/Nova.Playback/PlaybackGenerationMatchedSchedulerDeliveryBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputDispatcher.cs`

## 進捗記録の規律

実装・設計変更だけでなく、バグ修正にも同じ進捗記録の規律を適用します。

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.10d Phase 4 — Explicit Physical Run Composition**

runner harness、approved external-runtime lifecycle、exact runtime manifest、controlled fixture、Phase 2 native counter、Phase 1 evidence gateを一つの明示command／entrypointへ有限compositionします。videoとaudioは独立runとし、evidence captureはoutput cleanup前、cleanupはproduction-firstで行います。accepted `SystemWindows` recordが得られるまでphysical validation完了を記録しません。

## 未完了

- explicit Windows physical run command／entrypoint
- controlled physical Windows video output validation
- controlled physical Windows audio output validation
- combined controlled audible／visible playback validation
- GUI legal／about surface
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- GUI、installer、recorder
