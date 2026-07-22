# PeerCast Nova Navigation

## 現在地

**0.0.10c — Controlled Windows Video／Audio Output Validation Review — Completed**

0.0.10bのcontrolled native H.264／AAC decodeを前提に、real D3D11／DXGI video presentationとreal WASAPI audio outputを有限検証するためのownership、証跡、失敗条件、artifact非保持ルールを確定しました。0.0.10c自体はreview-onlyでありphysical outputはまだ実行していません。

- applicationがreal window／message loop／owner-thread dispatcherを所有
- playback lifecycleがprobe、COM／D3D11／DXGI／WASAPI、binding、stream、buffer、productionを所有
- exact approved external FFmpeg runtimeとcontrolled fixtureのみ使用
- videoはgeneration-matched deliveryとphysical present acknowledgementを必須化
- audioはgeneration-matched deliveryとdeterministic WASAPI buffer progressを必須化
- `WouldBlock`、resize、resetはcaller-visibleでhidden retryなし
- device lossはterminalでadapter／endpoint fallbackやin-place recreateなし
- video／audioは独立runを許可し、両方通過後にcombined runへ進む
- production-first、owner-thread output teardown、borrowed window／dispatcher維持
- runtime binary、fixture、decoded bytes、native pointer、device identifierを記録しない

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終headでrestore、Release build with analyzers、full solution tests、format verificationを通してからmergeします。

## 最新の入口

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

**0.0.10d — Controlled Physical Windows Video／Audio Output Validation**

ADR-0138／PCN-0146の条件に従い、明示Windows 11 physical runner、real application-owned window／dispatcher、exact approved external runtime、controlled fixtureを使ってvideoとaudioのphysical outputを独立した有限runで検証します。production ownership、generation matching、caller-driven backpressure、terminal device loss、artifact非保持を変更しません。

## 未完了

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
