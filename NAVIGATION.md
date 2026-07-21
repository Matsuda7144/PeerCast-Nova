# PeerCast Nova Navigation

## 現在地

**0.0.9p — Generation-matched Scheduler Delivery and Output Lifecycle Review — Completed**

0.0.9oのscheduler-owned frameを、同じsource-bound decoder generationのcontextとoutput lifecycleへ接続するownership契約を確定しました。runtime実装は0.0.9qへ分離しています。

- production deliveryはscheduler reservation経路へ一本化し、destructiveな`RunOneAsync`は診断用途に維持
- 一回の有限operationでreserve→generation検証→matching context選択→timing／reset admission→conversion→output→completion
- native video／audio contextはsource-bound native sessionだけが所有し、二重ownershipを禁止
- result-aware canonical output seamでretryable Windows output状態を保持
- reservation completionを`Delivered / Dropped / Retained`へ分離
- first-frame `DecoderReset`は成功したreset admission後に一度だけacknowledge
- active delivery／reservationをcancel／awaitしてからscheduler frameとnative contextをcleanup
- device-wide Windows activation／binding／initialization ownershipは外側に維持
- second queue、resident loop、hidden retry、同一frameへの二重output pathなし

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0126-generation-matched-scheduler-delivery-output-lifecycle.md`
- `docs/reviews/PCN-0134-GENERATION-MATCHED-SCHEDULER-DELIVERY-OUTPUT-LIFECYCLE-REVIEW.md`
- `src/Nova.Playback/PlaybackDecodedCompletionSchedulerIngressBoundary.cs`
- `src/Nova.Playback/PlaybackSourceBoundOneFrameDecodeBoundary.cs`
- `src/Nova.Playback/PlaybackBoundedFlvNativeDecoderSession.cs`
- `src/Nova.Playback/BoundedPlaybackScheduler.cs`
- `src/Nova.Playback/PlaybackLateAwareTimedDispatchBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputBufferProcessingBoundary.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9q — Finite Generation-matched Scheduler Delivery and Output Lifecycle Boundary**

exact source-bound decode generation、0.0.9o scheduler ingress、one-item reservation、matching context conversion、result-aware canonical output、one-shot reset admission、typed reservation completion、async replacement cleanupを一つの有限generation boundaryへ実装します。

## 未完了

- finite generation-matched scheduler delivery and output lifecycle implementation
- production generation factory and backend activation
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- runtime distribution decision
- controlled audible／visible playback
- GUI、installer、recorder
