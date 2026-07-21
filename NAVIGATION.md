# PeerCast Nova Navigation

## 現在地

**0.0.9l — Awaited Decoded Completion Composition Review — Reviewed**

0.0.9iのawaited PCP／FLV compositionを0.0.9kのsource-bound one-frame decode boundaryへ安全に置き換え、decoded completionをsettleしてからencoded retry／parser suffixを再開する所有権契約を確定しました。

- future generationは別々のnative session＋submission machineを持たず、0.0.9k boundaryを唯一のsubmission／receive authorityとして所有
- decoded completion settlementはasync operationの未完了そのものをbackpressureとして表現
- settlement完了はconsumed、transferred、rejected、cancelled、faultedの固定結果
- packet／flush reliefではPCP callbackまたはexplicit finalizationを開いたまま、1 receive→1 settlement→completion release→1 retry-only transition
- repeated `EAGAIN`はshared frame／byte／advance budget内で次の有限cycleを要求
- parser suffix、新しいPCP packet、別receiveはactive decoded ownership中に進めない
- ordinary post-accept receiveはcallback外の1回1frame caller operation
- accepted flush後のvideo／audio drainもcallback外で独立して1回1frame、各decoder EOFまで継続
- encoded input finalized、decoder drained、downstream ownership remainingを別状態として扱う
- `BoundedPlaybackScheduler`だけをdecoded queueとして再利用し、composition queueを追加しない
- 0.0.9kのvideo／audio別sequenceとは別に、scheduler用generation-wide cross-media sequenceをchecked commit
- scheduler transferにはone-shot managed-frame detachが必要で、accepted transfer後はcompletion disposalがscheduler-owned bufferを消さない
- queued frameは同じdecoder generationのcontext lifetimeに束縛し、discontinuity時はold scheduler ownershipをnative sessionより先に解放
- cleanupはactive settlement→decoded completion→scheduler ownership→decode boundary→parser／source→factory

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0122-awaited-decoded-completion-composition.md`
- `docs/reviews/PCN-0130-AWAITED-DECODED-COMPLETION-COMPOSITION-REVIEW.md`
- `src/Nova.Playback/PlaybackPcpFlvCompositionBoundary.cs`
- `src/Nova.Playback/PlaybackSourceBoundOneFrameDecodeBoundary.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegAwaitedSinkDelivery.cs`
- `src/Nova.Playback/PlaybackDiagnosticManagedFrameSchedulerIngressTransaction.cs`
- `src/Nova.Playback/BoundedPlaybackScheduler.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9m — Finite Awaited Decoded Completion Composition Boundary**

`PlaybackPcpFlvCompositionGeneration`を0.0.9k boundary所有へ置き換え、注入式awaited settlement seamを追加します。PCP callback内のretained packet／flush relief、callback外のordinary receive、explicit finalizationとvideo／audio drain、shared work budget、cancellation、discontinuity、cleanupをcross-platform fake consumerで検証します。real scheduler ingress／sink deliveryとproduction ABI-gate acquisitionは後続へ分離します。

## 未完了

- finite awaited PCP／FLV／decode composition implementation
- real FLV decoded completionのscheduler ingress／awaited delivery
- production generation factory and backend activation
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- runtime distribution decision
- controlled audible／visible playback
- GUI、installer、recorder