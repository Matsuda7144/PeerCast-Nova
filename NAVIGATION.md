# PeerCast Nova Navigation

## 現在地

**0.0.9z — Controlled Retained-Fixture Production Activation Validation — Validated**

0.0.9fのcontrolled FLV fixtureを0.0.9y application lifecycleへ接続し、実bounded parser、実production composition、source-bound decode、decoded completion transfer、retained scheduler ownership、generation-matched output admissionを一つのcross-platform harnessで検証しました。

- real FLV bootstrapからexact parser source sessionとdecoder generation 0を作成
- production snapshotから320×240 videoと48 kHz stereo audio構成を導出
- lifecycle outputをexact decoder generation 0へ構成して`Ready`へ移行
- interleaved video／audio packetのaudio submit `EAGAIN`を保持
- receive 1回 → managed audio frame 1個 → scheduler transfer 1回 → retained retry 1回
- second encoded／decoded queue、hidden loop、implicit retryなし
- first deliveryの`RetainedWouldBlock`でscheduler ownershipを維持
- caller-driven second deliveryで`AudioDelivered`をcommitしframeを解放
- real discontinuity bootstrapでold generation stop完了後にgeneration 1を作成
- output-ready generationを0から1へreconfigureし再び`Ready`
- normal stopはproduction-first、その後controlled outputを解放
- borrowed dispatcherとwindow tokenをcleanup対象外として維持
- physical Windows／real FFmpeg activationは注入のまま別gateとして維持
- routine CircleCIでrestore、Release build with analyzers、full tests、format verification成功

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終headでrestore、Release build with analyzers、full solution tests、format verificationを通してからmergeします。

## 最新の入口

- `tests/Nova.Playback.Tests/PlaybackControlledRetainedFixtureProductionActivationValidationTests.cs`
- `docs/adr/ADR-0136-controlled-retained-fixture-production-activation-validation.md`
- `docs/reviews/PCN-0144-CONTROLLED-RETAINED-FIXTURE-PRODUCTION-ACTIVATION-VALIDATION.md`
- `docs/adr/ADR-0135-finite-approved-external-runtime-windows-lifecycle-activation-boundary.md`
- `docs/reviews/PCN-0143-FINITE-APPROVED-EXTERNAL-RUNTIME-WINDOWS-LIFECYCLE-ACTIVATION-BOUNDARY.md`
- `src/Nova.Playback/PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary.cs`
- `src/Nova.Playback/PlaybackApprovedExternalRuntimeWindowsLifecycleOperations.cs`
- `src/Nova.Playback/PlaybackDispatcherBackedProductionGeneration.cs`
- `src/Nova.Playback/PlaybackWindowsOutputDispatcher.cs`
- `src/Nova.Playback/PlaybackProductionPcpFlvCompositionBoundary.cs`
- `src/Nova.Playback/PlaybackGenerationMatchedSchedulerDeliveryBoundary.cs`
- `tests/Nova.Playback.Tests/PlaybackControlledRetainedFlvFixtureValidationTests.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.10a — Controlled Real External-Runtime Decode Activation Review**

exact approved built-in runtimeを実際に配置したcontrolled Windows環境でnative decode activationを行う前に、fixture provenance、runtime-directory custody、Windows runner、decoder input／output evidence、cleanup、artifact非配布、失敗時の証跡をレビューします。physical rendering／audio outputは後続gateとして分離します。

## 未完了

- controlled real external-runtime decode activation review
- controlled real FFmpeg native decode validation
- controlled Windows video／audio output validation
- Windows application window／message loop
- application dispatcher implementation
- GUI legal／about surface
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- controlled audible／visible playback
- GUI、installer、recorder
