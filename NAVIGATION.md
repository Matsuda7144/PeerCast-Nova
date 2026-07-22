# PeerCast Nova Navigation

## 現在地

**0.0.10d — Controlled Physical Windows Video／Audio Output Validation — Phase 1 In Progress**

ADR-0138／PCN-0146で定義したphysical output gateの最初の実装として、sanitized deterministic evidenceをone-shotで受理するfail-closed boundaryを追加しました。`SystemWindows`以外の証跡はphysical outputとして受理せず、実機runはまだ実行していません。

- exact active runtime build IDとcontrolled fixture SHA-256を必須化
- decoder generationとoutput generationの完全一致を必須化
- videoはBGRA32、exact stride、present acknowledgement、accepted bytesを検証
- audioはinterleaved PCM、buffer duration、WASAPI write、accepted frames／bytes、first startを検証
- delivered／retained `WouldBlock`をfinite attempt count内で検証
- hidden retry、device fallback、device recreationをゼロに固定
- production stop → buffer → stream → binding → activationのcleanup順序を必須化
- owner-thread teardownとborrowed window／dispatcher維持を必須化
- one-shot accepted／rejected settlement、rollback／in-place correctionなし
- runtime path、device identifier、native pointer、handle、packet／decoded bytesを証跡型へ含めない
- Phase 1はevidence contractのみでphysical D3D11／WASAPI実行を主張しない

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終headでrestore、Release build with analyzers、full solution tests、format verificationを通してからmergeします。

## 最新の入口

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

**0.0.10d Phase 2 — System Native Evidence Capture and Physical Runs**

既存の`IDXGISwapChain.Present`とWASAPI `ReleaseBuffer／Start`経路からsanitized counterを直接取得し、application-owned real window／dispatcherとexact approved runtime／fixtureを使用する独立video／audio runを明示Windows 11 physical runnerで実行します。Phase 1 evidence gateを迂回する別経路は追加しません。

## 未完了

- system-native present／WASAPI evidence counter connection
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
