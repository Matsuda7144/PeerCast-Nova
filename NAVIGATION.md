# PeerCast Nova Navigation

## 現在地

**0.0.9n — Decoded Completion Transfer and Scheduler Ingress Review — Reviewed**

0.0.9mのsource-bound decoded completionを既存`BoundedPlaybackScheduler`へ安全に移譲する所有権契約を確定しました。

- scheduler `Enqueue`は成功／拒否を問わずincoming frameを消費または破棄するため、enqueue後のrollbackを禁止
- exact active completionだけがmanaged frameをone-shot detach可能
- detach前にsource、decoder／discontinuity generation、media、stream、sequence、managed bytes、frame livenessを検証
- completion disposalはdetach後のscheduler-owned frameを破棄しない
- scheduler ingressは1 frameにつき1回だけenqueueし、再構築／retryなし
- accepted transferは`Enqueued`または`EnqueuedAfterDroppingOldest`だけ
- rejected／dropped／invalid／sequence-rejected transferはgeneration terminal
- video／audio別stream-local sequenceはidentityとして保持し、scheduler用cross-media sequenceを別管理
- downstream sequenceと最初の`DecoderReset` markerはaccepted enqueue後だけcommit
- queued frameが残る間、matching decoder-generation context lifetimeをgeneration側が保持
- discontinuity時はadmission close→active completion→scheduler queue／reservation→decode boundary／native sessionの順で解放
- direct awaited consumptionとscheduler transferを同じframeへ二重適用しない

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0124-decoded-completion-transfer-scheduler-ingress.md`
- `docs/reviews/PCN-0132-DECODED-COMPLETION-TRANSFER-SCHEDULER-INGRESS-REVIEW.md`
- `src/Nova.Playback/PlaybackPcpFlvAwaitedDecodeCompositionBoundary.cs`
- `src/Nova.Playback/PlaybackSourceBoundOneFrameDecodeBoundary.cs`
- `src/Nova.Playback/PlaybackDiagnosticManagedFrameSchedulerIngressTransaction.cs`
- `src/Nova.Playback/BoundedPlaybackScheduler.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9o — One-shot Decoded Completion Transfer and Scheduler Ingress Boundary**

`PlaybackSourceBoundDecodedFrameCompletion`へgeneration-controlled one-shot detachを追加し、注入式scheduler ingressでcross-media sequence、first-frame reset marker、accepted／rejected ownership、context lifetime、discontinuity cleanupをcross-platform focused testへ落とします。

## 未完了

- one-shot decoded completion transfer／bounded scheduler ingress implementation
- generation-matched scheduler delivery and output lifecycle
- production generation factory and backend activation
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- runtime distribution decision
- controlled audible／visible playback
- GUI、installer、recorder