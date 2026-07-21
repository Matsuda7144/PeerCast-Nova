# PeerCast Nova Navigation

## 現在地

**0.0.9r — Production Decoder Generation Factory and Activation Review — Completed**

0.0.9qのcomplete finite generationをproductionで構築・置換するownership、policy、activation境界を確定しました。runtime factory実装は0.0.9sへ分離しています。

- license／backend policyをruntime path確認とnative workより先に適用
- pinned candidateは`LicenseReview.Pending`のまま維持し、production creationをfail closed
- explicit host-installed exact pinned runtimeだけを使用
- `PATH`／registry／network search、download、extraction、bundlingなし
- video／audioに独立したruntime→ABI gate→context chainを要求
- shared runtime lease／shared gateを禁止
- exact FLV bindingからcomplete native sessionだけを返す
- decode、fresh scheduler、timing、0.0.9q deliveryを一つのgeneration productへ統合
- source demux sessionとWindows output environmentは非所有borrow
- old generationを完全停止してからreplacementを作成
- normal backend registration、license approval、device activationは変更しない
- resident loop、hidden retry、fallback runtime、second queueなし

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0128-production-decoder-generation-factory-activation-review.md`
- `docs/reviews/PCN-0136-PRODUCTION-DECODER-GENERATION-FACTORY-ACTIVATION-REVIEW.md`
- `src/Nova.Playback/PlaybackFfmpegBackendCandidate.cs`
- `src/Nova.Playback/PlaybackDecoderBackendPolicy.cs`
- `src/Nova.Playback/PlaybackDecoderBackendInstalledRuntimeLocator.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegAbiSymbolGate.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegContextLease.cs`
- `src/Nova.Playback/PlaybackBoundedFlvNativeDecoderSession.cs`
- `src/Nova.Playback/PlaybackGenerationMatchedSchedulerDeliveryBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputActivationBoundary.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9s — Policy-gated Production Decoder Generation Factory Boundary**

policy-first fail-closed、exact managed binding、独立したvideo／audio runtime・ABI gate chain、complete native session、decode／scheduler／timing／0.0.9q delivery product、reverse rollbackを一つの有限factoryへ実装します。production candidateの`Pending`状態とapplication-owned Windows resourcesは維持します。

## 未完了

- policy-gated production generation factory implementation
- FFmpeg license approval and backend registration
- controlled production decode ingress composition
- Windows application／window／device lifecycle
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- runtime distribution decision
- controlled audible／visible playback
- GUI、installer、recorder
