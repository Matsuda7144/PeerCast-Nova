# PeerCast Nova Navigation

## 現在地

**0.0.9s — Policy-gated Production Decoder Generation Factory Boundary — Implemented**

0.0.9rで確定したpolicy-first ownership契約をruntimeへ実装し、approvedな注入条件下だけでcomplete source-bound decode／scheduler-delivery generationを構築する有限factoryを追加しました。

- provenance／backend policyをbinding・filesystem・native workより先に評価
- pinned candidateは`LicenseReview.Pending`のまま維持
- public production creationは`LicenseReviewPending`でfail closed
- explicit host-installed exact pinned runtimeだけを使用
- video／audioに独立したruntime→ABI gate→context chainを作成
- shared runtime／shared gateを参照同一性で拒否
- exact FLV bindingからcomplete native sessionだけを返す
- decode、fresh bounded scheduler、timing、0.0.9q deliveryを一つのgeneration productへ統合
- partial failureは未移譲ownershipだけを逆順・重複排除cleanup
- source demux sessionとWindows output boundaryは非所有borrow
- returned productはfinite transfer／delivery／reset／stopを公開
- factory disposalはactive creationをcancel／await
- completed generationはcaller ownershipでfactory非所有
- normal backend registration、license approval、device activationは変更しない
- resident loop、hidden retry、fallback runtime、second queueなし

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0129-policy-gated-production-decoder-generation-factory-boundary.md`
- `docs/reviews/PCN-0137-POLICY-GATED-PRODUCTION-DECODER-GENERATION-FACTORY-BOUNDARY.md`
- `src/Nova.Playback/PlaybackProductionDecoderGenerationFactory.cs`
- `tests/Nova.Playback.Tests/PlaybackProductionDecoderGenerationFactoryTests.cs`
- `src/Nova.Playback/PlaybackFfmpegBackendCandidate.cs`
- `src/Nova.Playback/PlaybackDecoderBackendPolicy.cs`
- `src/Nova.Playback/PlaybackDecoderBackendInstalledRuntimeLocator.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegAbiSymbolGate.cs`
- `src/Nova.Playback/PlaybackBoundedFlvNativeDecoderSession.cs`
- `src/Nova.Playback/PlaybackSourceBoundOneFrameDecodeBoundary.cs`
- `src/Nova.Playback/PlaybackGenerationMatchedSchedulerDeliveryBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputBufferProcessingBoundary.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9t — Production Generation Ingress and Replacement Composition Review**

0.0.9s generation productをawaited PCP／FLV compositionへ注入し、decoded completion transfer、finite delivery、old generation停止、parser discontinuity、new generation creationをどう直列化するかレビューします。application-owned Windows lifecycle、terminal failure、cancellation、replacement rollback禁止の境界を確定します。

## 未完了

- production generation ingress and replacement composition
- FFmpeg license approval and backend registration
- controlled production decoder ingress activation
- Windows application／window／device lifecycle
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- runtime distribution decision
- controlled audible／visible playback
- GUI、installer、recorder
