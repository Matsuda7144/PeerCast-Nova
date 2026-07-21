# PeerCast Nova Navigation

## 現在地

**0.0.9t — Production Generation Ingress and Replacement Composition Review — Completed**

0.0.9s generation productをawaited PCP／FLV経路へ接続するownership、transfer、delivery、replacement、terminal cleanup契約を確定しました。runtime実装は0.0.9uへ分離しています。

- consumed settlementとscheduler transferのownership契約が非互換
- `Transferred`後のcompletion再検証／disposeを禁止
- 既存awaited decode compositionは診断・比較用として変更しない
- production専用compositionを新設する方針
- production productへdecode snapshot／submission／one-frame receiveを内部forward
- raw decode／native／runtime ownerを外へ公開しない
- packet、receive／drain、transfer、delivery、reset、replacement、stopを一つのgateで直列化
- caller-driven deliveryは一予約ずつ、PCP callback外で実行
- retained output resultはscheduler reservationを保持
- scheduler rejectionはterminalで、hidden capacity retryやsecond queueなし
- discontinuityはold generation停止完了後にparser reset
- new generationはreplacement source description後だけ作成
- overlap、old revival、rollback、partial packet commitなし
- source parserとWindows outputはapplication ownershipを維持
- pinned candidateは`LicenseReview.Pending`のまま
- runtime source／tests変更なし

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0130-production-generation-ingress-replacement-composition-review.md`
- `docs/reviews/PCN-0138-PRODUCTION-GENERATION-INGRESS-REPLACEMENT-COMPOSITION-REVIEW.md`
- `src/Nova.Playback/PlaybackProductionDecoderGenerationFactory.cs`
- `src/Nova.Playback/PlaybackPcpFlvAwaitedDecodeCompositionBoundary.cs`
- `src/Nova.Playback/PlaybackOneEventFinalEosFlvParser.cs`
- `src/Nova.Playback/PlaybackDecodedCompletionSchedulerIngressBoundary.cs`
- `src/Nova.Playback/PlaybackGenerationMatchedSchedulerDeliveryBoundary.cs`
- `src/Nova.Playback/PlaybackSourceBoundOneFrameDecodeBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputBufferProcessingBoundary.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9u — Finite Production Generation Ingress and Replacement Composition Boundary**

production専用compositionを実装し、0.0.9s productへexact one-event submission／one-frame receiveを内部forwardします。一度だけのdecoded transfer、caller-driven one-item delivery、explicit reset、asynchronous stop、old-stop-before-parser-reset replacementを一つの有限境界へ統合します。

## 未完了

- finite production generation ingress and replacement composition implementation
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
