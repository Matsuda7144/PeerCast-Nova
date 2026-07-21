# PeerCast Nova Navigation

## 現在地

**0.0.9q — Finite Generation-matched Scheduler Delivery and Output Lifecycle Boundary — Implemented**

0.0.9pで確定したownership契約をruntimeへ実装し、scheduler-owned real decoded frameを同じsource-bound decoder generationのcontext、timing、managed conversion、result-aware Windows outputへ接続する有限境界を追加しました。

- exact source、decoder／discontinuity generation、scheduler sequence、media role、frame livenessをreservation中に検証
- source-bound native sessionをvideo／audio contextの唯一のownerとして維持
- matching H.264／AAC contextだけでadapt／convert
- caller-driven one-item deliveryのみ。resident loop／hidden retryなし
- reservation completionを`Retained / Delivered / Dropped`へ型付き拡張
- 旧boolean completionとの互換を維持
- late videoはdrop accounting、late audioはrecovery-required retention
- `WouldBlock`、initialization／binding／resize、cancellationはframeを保持
- device lossはframeを保持してreset-requiredへ遷移
- first-frame `DecoderReset`はtiming／output reset成功後に一度だけacknowledge
- active delivery中の後続completion detachを禁止
- async stopはdeliveryをcancel／awaitしてからscheduler→decode boundary→native sessionの順にcleanup
- production factory、backend activation、Windows device lifecycleは追加しない

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0127-finite-generation-matched-scheduler-delivery-output-lifecycle-boundary.md`
- `docs/reviews/PCN-0135-FINITE-GENERATION-MATCHED-SCHEDULER-DELIVERY-OUTPUT-LIFECYCLE-BOUNDARY.md`
- `src/Nova.Playback/PlaybackGenerationMatchedSchedulerDeliveryBoundary.cs`
- `src/Nova.Playback/PlaybackDecodedCompletionSchedulerIngressBoundary.cs`
- `src/Nova.Playback/PlaybackSourceBoundOneFrameDecodeBoundary.cs`
- `src/Nova.Playback/PlaybackBoundedFlvNativeDecoderSession.cs`
- `src/Nova.Playback/BoundedPlaybackScheduler.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegDecodedOutputAdapter.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegManagedFormatConversion.cs`
- `src/Nova.Playback/PlaybackWindowsOutputBufferProcessingBoundary.cs`
- `tests/Nova.Playback.Tests/PlaybackGenerationMatchedSchedulerDeliveryBoundaryTests.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9r — Production Decoder Generation Factory and Activation Review**

verified runtime locator／lease、ABI gate、FLV parameter binding、source-bound native session、decode／scheduler delivery generation、timing、Windows output lifecycleをproduction factoryがどう取得・所有・置換するかをレビューします。backend activation、license gate、failure rollback、discontinuity cleanup、application-owned device resourcesの境界を確定します。

## 未完了

- production generation factory and backend activation
- ABI-gate acquisition and license-policy composition
- Windows application／window／device lifecycle
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- runtime distribution decision
- controlled audible／visible playback
- GUI、installer、recorder
