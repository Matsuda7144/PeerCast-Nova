# PeerCast Nova Navigation

## 現在地

**0.0.10d — Controlled Physical Windows Video／Audio Output Validation — Phase 2 Counter Plumbing In Progress**

Phase 1のone-shot admission gateへ、既存のsystem Windows output processorからsanitized deterministic counterを渡す経路を追加しました。production buffer snapshotにnative evidenceを載せ、同じPhase 1 gateへ変換するadapterを実装しています。実機runはまだ実行していません。

- supported Windowsのproduction processorだけを`SystemWindows` provenanceで包む
- injected processorを自動的にsystem provenanceへ昇格させない
- videoはattempt、present acknowledgement、accepted bytes、`WouldBlock`、resize、device lossを記録
- audioはattempt、write acknowledgement、accepted frames／bytes、`WouldBlock`、first start、device lossを記録
- first successful configurationとgenerationを固定し、後続矛盾をlatch
- counter overflowをlatchし、native result自体は変更しない
- additiveな`PlaybackWindowsOutputBufferSnapshot.NativeEvidence`で既存lifecycleへ接続
- native generation／buffer generation／retained `WouldBlock`の矛盾をcapture adapterで拒否
- adapterは必ずPhase 1 `PlaybackControlledWindowsOutputEvidenceBoundary`を通す
- native ownership、scheduler ownership、reset、resize、device-loss、cleanup順序は変更しない
- Phase 2はcounter plumbingのみでphysical D3D11／WASAPI実行を主張しない

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終headでrestore、Release build with analyzers、full solution tests、format verificationを通してからmergeします。

## 最新の入口

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
- `src/Nova.Playback/PlaybackDecoderFfmpegFrameDataBoundary.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegManagedFrameCopy.cs`
- `docs/adr/ADR-0137-approved-ffmpeg-candidate-manifest-refresh.md`
- `docs/reviews/PCN-0145-APPROVED-FFMPEG-CANDIDATE-MANIFEST-REFRESH.md`
- `THIRD-PARTY-LICENSES/FFmpeg/PROVENANCE.md`

## 進捗記録の規律

実装・設計変更だけでなく、バグ修正にも同じ進捗記録の規律を適用します。

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.10d Phase 3 — Explicit Windows Physical Runner Harness**

application-owned real window／message loop／owner-thread dispatcherを持つ有限runnerを明示Windows 11環境向けに用意し、exact approved runtimeとcontrolled fixtureを使う独立video／audio runを実行します。pre-cleanup native evidence snapshot、caller-visible delivery counters、production-first cleanup証跡をPhase 1 gateへ提出し、accepted recordが得られた場合だけphysical validation完了を記録します。

## 未完了

- explicit Windows physical runner harness
- controlled physical Windows video output validation
- controlled physical Windows audio output validation
- combined controlled audible／visible playback validation
- Windows application window／message loop implementation
- application dispatcher implementation
- GUI legal／about surface
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- GUI、installer、recorder
