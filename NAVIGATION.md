# PeerCast Nova Navigation

## 現在地

**0.0.9y — Finite Approved External Runtime and Windows Lifecycle Activation Boundary — Implemented**

ADR-0134で確定したapplication-owned lifecycleを実装し、exact built-in approvedまたはexplicit host-attested external FFmpeg production routeを既存Windows 11 D3D11／WASAPI output境界へ有限かつowner-thread-safeに接続しました。

- application-owned dispatcherとpositive opaque window tokenを借用
- playback側ではwindow、thread、queue、message pump、timer、resident loopを作成しない
- pure preflightをprobe、dispatcher、runtime path、native accessより先に実行
- probe → owner-thread activation → binding／stream／buffer → production compositionの固定順序
- exact parser stream description後にactive decoder generationでvideo／audio outputを構成
- 0.0.9q generation-matched schedulerを唯一のproduction delivery ownerとして維持
- async production delivery／resetをdispatcher-backed output pathへ限定
- discontinuity replacementでaudio buffer → video buffer → audio stream → video stream → bindingをreset
- new generationをvideo bind → audio bind → video initialize → audio initializeの順に構成
- `WouldBlock`／wait／binding／initialization／resize等はcaller-visibleでscheduler itemを保持
- resizeはsame-generation explicit operationでありhidden retryなし
- device lossはterminal、production-firstでoutputをowner thread上で解放
- stop／cancellation／partial construction cleanupをidempotentに固定
- borrowed dispatcher／window tokenをcleanup対象外として維持
- cross-platform injected tests 10件で順序、ownership、cancellation、partial cleanupを検証
- routine CircleCIでrestore、Release build with analyzers、full tests、format verification成功

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終headでrestore、Release build with analyzers、full solution tests、format verificationを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0135-finite-approved-external-runtime-windows-lifecycle-activation-boundary.md`
- `docs/reviews/PCN-0143-FINITE-APPROVED-EXTERNAL-RUNTIME-WINDOWS-LIFECYCLE-ACTIVATION-BOUNDARY.md`
- `src/Nova.Playback/PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary.cs`
- `src/Nova.Playback/PlaybackApprovedExternalRuntimeWindowsLifecycleOperations.cs`
- `src/Nova.Playback/PlaybackDispatcherBackedProductionGeneration.cs`
- `src/Nova.Playback/PlaybackWindowsOutputDispatcher.cs`
- `tests/Nova.Playback.Tests/PlaybackApprovedExternalRuntimeWindowsLifecycleBoundaryTests.cs`
- `tests/Nova.Playback.Tests/PlaybackApprovedExternalRuntimeWindowsLifecycleFailureTests.cs`
- `docs/adr/ADR-0134-approved-external-runtime-windows-lifecycle-activation-review.md`
- `docs/reviews/PCN-0142-APPROVED-EXTERNAL-RUNTIME-WINDOWS-LIFECYCLE-ACTIVATION-REVIEW.md`
- `docs/adr/ADR-0133-external-lgpl-runtime-compliance-user-replacement-boundary.md`
- `docs/reviews/PCN-0141-EXTERNAL-LGPL-RUNTIME-COMPLIANCE-USER-REPLACEMENT-BOUNDARY.md`
- `src/Nova.Playback/PlaybackFfmpegExternalRuntimeProductionComposition.cs`
- `src/Nova.Playback/PlaybackGenerationMatchedSchedulerDeliveryBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputActivationBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputResourceBindingBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputStreamInitializationBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputBufferProcessingBoundary.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9z — Controlled Retained-Fixture Production Activation Validation**

保持済みのcontrolled FLV fixtureと明示runtime環境を使い、0.0.9y lifecycleのproduction activationを有限手順で検証します。runtime verification、exact stream description、generation creation、output-configuration admissionまでを対象とし、physical Windows outputやaudible／visible playbackは別gateとして残します。

## 未完了

- controlled retained-fixture production activation validation
- controlled real-media decode／output validation
- Windows application window／message loop
- application dispatcher implementation
- GUI legal／about surface
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- controlled audible／visible playback
- GUI、installer、recorder
