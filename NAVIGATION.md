# PeerCast Nova Navigation

## 現在地

**0.0.9o — One-shot Decoded Completion Transfer and Scheduler Ingress Boundary — Implemented**

0.0.9nで確定したownership契約をruntimeへ落とし、exact active decoded completionから既存`BoundedPlaybackScheduler`へmanaged frameを1回だけ移譲する境界を追加しました。

- completion ownershipをattached／detached／disposedの3状態で管理
- exact active decode boundaryがsource、decoder／discontinuity generation、media、stream、stream-local sequence、managed bytes、frame roleを再検証してdetachを承認
- detach後のcompletion disposalはdecode backpressureだけを解放し、scheduler-owned frameを破棄しない
- scheduler ingressはvalidate→detach→enqueueを1回だけ実行し、rollback／reconstruction／retryなし
- video／audio別stream-local sequenceはidentityとして保持
- scheduler用generation-wide sequenceはaccepted enqueue後だけcommit
- replacement generationの最初のaccepted frameだけ`DecoderReset`
- cancellation before detachはcompletion ownershipとreset markerを維持
- rejected／undefined／invalid／exhausted／exception pathはgeneration terminal
- terminal／discontinuity cleanupはscheduler ownership→matching decoder context lifetimeの順
- second decoded queue、resident loop、production delivery／output activationは追加しない

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0125-one-shot-decoded-completion-scheduler-ingress-boundary.md`
- `docs/reviews/PCN-0133-ONE-SHOT-DECODED-COMPLETION-SCHEDULER-INGRESS-BOUNDARY.md`
- `src/Nova.Playback/PlaybackDecodedCompletionSchedulerIngressBoundary.cs`
- `src/Nova.Playback/PlaybackSourceBoundOneFrameDecodeBoundary.cs`
- `src/Nova.Playback/BoundedPlaybackScheduler.cs`
- `tests/Nova.Playback.Tests/PlaybackDecodedCompletionSchedulerIngressBoundaryTests.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9p — Generation-matched Scheduler Delivery and Output Lifecycle Review**

scheduler-owned frame、generation-matched decoder context lifetime、reservation、late-aware dispatch、awaited sink／Windows output lifecycleをどのgeneration ownerで接続するかをレビューします。delivery完了、drop、stop、discontinuity、output failureのownershipとcleanup順を確定し、runtime compositionは次の実装マイルストーンへ分離します。

## 未完了

- generation-matched scheduler delivery and output lifecycle
- production generation factory and backend activation
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- runtime distribution decision
- controlled audible／visible playback
- GUI、installer、recorder
