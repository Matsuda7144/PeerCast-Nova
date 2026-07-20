# PeerCast Nova Navigation

## 現在地

**0.0.9i — Finite Awaited PCP-to-FLV Composition Boundary — Implemented**

awaited PCP packet callbackからone-event FLV parser façade、exact source-bound decoder generation、finite submission state machineまでを1つのserialized boundaryとして接続しました。

- `PlaybackPcpFlvCompositionBoundary`が`IPcpChannelPacketConsumer`を実装
- exact PCP channel ID／session ID／packet sequenceを検証
- callback payloadは借用のみで、callback完了後にpacket／memoryを保持しない
- callback内local offsetはparserの`ConsumedByteCount`だけで前進
- 1 callback内の複数FLV recordと、既存bounded carryによるsplit recordを処理
- parser completionを処理してから同じcallback suffixを再開
- `StreamDescriptionAvailable`後にinjected factoryからexact source-bound generationを作成
- generationはfinite submission state machineとnative sessionを所有し、parser sourceは非所有参照
- packet／flush `EAGAIN`は明示frame progress後のbounded `Advance`だけで進行
- no progress、accounting矛盾、identity／sequence／generation不一致、work limit超過をfail closed
- `CompleteInputAsync`だけがvideo／audio EOSを開始し、transport状態やempty packetをEOSと推測しない
- explicit discontinuityではold state machine／native sessionを解放後にparser generationを置換
- fixed enum snapshot、fixed-message failure、idempotent ordered cleanup
- second encoded queue、resident loop、production backend activation、decoded output wiringなし

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `src/Nova.Playback/PlaybackPcpFlvCompositionBoundary.cs`
- `tests/Nova.Playback.Tests/PlaybackPcpFlvCompositionBoundaryTests.cs`
- `tests/Nova.Playback.Tests/PlaybackPcpFlvCompositionFlushBackpressureTests.cs`
- `docs/adr/ADR-0119-finite-awaited-pcp-to-flv-composition-boundary.md`
- `docs/reviews/PCN-0127-FINITE-AWAITED-PCP-TO-FLV-COMPOSITION-VALIDATION.md`
- `src/Nova.Playback/PlaybackOneEventFinalEosFlvParser.cs`
- `src/Nova.Playback/PlaybackFiniteOneEventPacketSubmissionStateMachine.cs`
- `src/Nova.Network/Pcp/PcpChannelStreamReader.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9j — Bounded Decode Iteration and Per-stream EOS Drain Review**

0.0.9iでacceptedになったpacket／flush transitionからdecoded frameを受け取る有限iterationをレビューします。per-stream frame receive、packet acceptance後の追加出力、video／audio independent EOS drain、frame ownership、no-progress、operation budget、cancellation、discontinuity reset、cleanup順を先に確定し、runtime実装は後続milestoneへ分離します。

## 未完了

- bounded decode iteration and per-stream EOS drain review／implementation
- decoded-frame ownership and delivery for the real FLV path
- production generation factory and backend activation
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- runtime distribution decision
- controlled audible／visible playback
- GUI、installer、recorder
