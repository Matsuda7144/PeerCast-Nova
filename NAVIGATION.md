# PeerCast Nova Navigation

## 現在地

**0.0.9g — One-event Final EOS Sequencing — Implemented**

future composition用の有限FLV parser façadeを追加し、通常packetは既存parserへ委譲したまま、final EOSだけをexact source sessionから1操作1イベントで返すようにしました。

- final signalは全caller bytes消費後だけ受理
- final signalはempty operationまたは最後のcomplete packetと同時に指定可能
- incomplete carryを伴うfinal signalは`TruncatedData`でfail closed
- 両streamがopenならvideo EOSを先に、completion解放後にaudio EOSを返す
- 片方がexplicit EOS済みなら残りstreamだけを返す
- first EOSは`PacketsAvailable`、final EOSは`EndOfStream`
- final EOS completionはexact parser-created source sessionが所有
- sequenceは成功した実parser completionからだけ更新
- active completionが唯一のbackpressure owner
- detached EOS completion、copied EOS list、second encoded queueなし
- finalization開始後のnon-empty inputを拒否
- explicit discontinuityでsequence／ended／final stateをreset
- video／audioの両final flushが0.0.9e state machineを通過
- PCP composition、native FFmpeg、decoded output、backend activationは未接続

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `src/Nova.Playback/PlaybackOneEventFinalEosFlvParser.cs`
- `tests/Nova.Playback.Tests/PlaybackOneEventFinalEosFlvParserTests.cs`
- `docs/adr/ADR-0117-one-event-final-eos-sequencing.md`
- `docs/reviews/PCN-0125-ONE-EVENT-FINAL-EOS-SEQUENCING-VALIDATION.md`
- `src/Nova.Playback/PlaybackFiniteOneEventPacketSubmissionStateMachine.cs`
- `src/Nova.Playback/PlaybackBoundedFlvParser.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9h — PCP Chunk-to-FLV Parser Composition Review**

awaited PCP callbackが所有するchunk memoryをone-event FLV parser façadeへ安全に渡す有限compositionを設計します。consumed bytesとcaller suffix、active parser／decoder completionによるpause、final signal、cancellation、discontinuity generation、session replacement、cleanup順を先に確定し、runtime wiringは後続実装へ分離します。

## 未完了

- PCP chunk-to-FLV parser composition review／implementation
- bounded decode iteration and per-stream EOS drain
- decoded-frame ownership and delivery for the real FLV path
- MP4／fragmented MP4 parser
- production decoder ingress
- discontinuity reset semantics
- duration cutoff
- complete A/V synchronization
- runtime distribution decision
- controlled audible／visible playback
- GUI、installer、recorder
