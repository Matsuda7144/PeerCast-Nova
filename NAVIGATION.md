# PeerCast Nova Navigation

## 現在地

**0.0.9m — Finite Awaited Decoded Completion Composition Boundary — Implemented**

real bounded FLV parserと0.0.9k source-bound one-frame decode boundaryを、callback-scoped PCP payloadからcaller-driven EOS drainまで1つのserialized finite compositionとして接続しました。

- `PlaybackPcpFlvAwaitedDecodeCompositionBoundary`を新規追加し、既存0.0.9i boundaryは比較／互換用に維持
- generationは0.0.9k boundaryを唯一のpacket submission／native receive authorityとして所有
- PCP payload offsetはparserのexact `ConsumedByteCount`だけで前進
- retained packet／flush reliefはcallbackまたはexplicit finalization内で完結
- 1 relief cycleは最大1 receive→1 awaited settlement→completion release→1 retry-only transition
- settlement未完了中はcallback、parser suffix、retained retryが進まない
- repeated `EAGAIN`はshared decode-operation／frame-count／managed-byte budget内で次の有限cycleを要求
- 0.0.9m settlementはexact completionをawaitし、`Consumed`だけを成功として受理
- ordinary receiveとaccepted-flush drainはcallback外の1回1frame operation
- parser finalization、video／audio flush acceptance、各decoder EOF、full drainを別状態として保持
- videoとaudioを独立してdrainし、両decoder EOF後だけ`DecodersDrained`
- discontinuityはold settlement→decode generation→native audio→videoの順で解放して新generationを作成
- parser／decode／settlementを1 semaphoreで直列化し、second queue／resident loopなし
- 11 focused cross-platform testsでcallback blocking、retry ordering、budget、cancellation、EOS drain、replacementを検証
- CA2250はsubordinate enum／linked stopをcleanup後に変換するため、対象runtime fileだけ理由付きで抑制

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `src/Nova.Playback/PlaybackPcpFlvAwaitedDecodeCompositionBoundary.cs`
- `tests/Nova.Playback.Tests/PlaybackPcpFlvAwaitedDecodeCompositionBoundaryTests.cs`
- `tests/Nova.Playback.Tests/PlaybackPcpFlvAwaitedDecodeCompositionTestSupport.cs`
- `docs/adr/ADR-0123-finite-awaited-decoded-completion-composition.md`
- `docs/reviews/PCN-0131-FINITE-AWAITED-DECODED-COMPLETION-COMPOSITION-VALIDATION.md`
- `src/Nova.Playback/PlaybackSourceBoundOneFrameDecodeBoundary.cs`
- `src/Nova.Playback/PlaybackPcpFlvCompositionBoundary.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9n — Decoded Completion Transfer and Scheduler Ingress Review**

0.0.9mのawaited-consumption契約を弱めず、decoded completionからmanaged frameをone-shotでdetachして既存`BoundedPlaybackScheduler`へ移譲する契約をレビューします。stream-local decoded sequenceとgeneration-wide cross-media scheduler sequenceを分離し、accepted／rejected transfer、native context lifetime、partial failure、discontinuity時のold-generation queue cleanupを先に固定します。

## 未完了

- decoded completion one-shot transfer／bounded scheduler ingress
- real FLV decoded scheduler delivery and output lifecycle
- production generation factory and backend activation
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- runtime distribution decision
- controlled audible／visible playback
- GUI、installer、recorder
