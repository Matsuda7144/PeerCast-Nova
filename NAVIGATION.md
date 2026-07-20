# PeerCast Nova Navigation

## 現在地

**0.0.9h — PCP Chunk-to-FLV Parser Composition Review — Reviewed**

awaited PCP packet callbackから0.0.9g one-event FLV parser façadeと0.0.9e submission state machineまでの有限compositionをレビューし、callback memory、suffix、decoder backpressure、finalization、discontinuity、generation ownershipを確定しました。

- PCP readerはconsumer callbackをawaitし、その完了までpacket payloadを保持
- callback payloadは借用のみで、callback完了後に保持しない
- compositionはcallbackを開いたまま処理し、caller suffixを別queueへコピーしない
- local offsetはparserの`ConsumedByteCount`だけで前進
- parser completionがprefixで返った場合はcompletion処理後に同じcallback suffixを再開
- parser／decoder completionがactiveな間は新しいparser inputと次のPCP Atomを停止
- decoder `EAGAIN`はbounded `Advance`でのみ進行
- `NoReceiveProgress`、work budget超過、undefined resultはfail closed
- exact PCP channel／session／packet sequenceとexact parser-created source generationを要求
- `StreamDescriptionAvailable`後にbinding、native session、fresh state machineを1 generationとして作成
- transport close、reconnect、stop、timeout、cancellation、failureをEOSと推測しない
- ownerが別の明示finalization操作でvideo／audio EOSを1件ずつ送る
- explicit discontinuityではretained submission、state machine、native sessionを解放後にparserをreset
- reconnectだけではparser／decoder generationをresetしない
- second encoded queue、caller-suffix retention、resident loop、runtime wiringなし

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0118-pcp-chunk-to-flv-parser-composition.md`
- `docs/reviews/PCN-0126-PCP-CHUNK-TO-FLV-PARSER-COMPOSITION-REVIEW.md`
- `src/Nova.Network/Pcp/PcpChannelStreamReader.cs`
- `src/Nova.Network/Pcp/IPcpChannelPacketConsumer.cs`
- `src/Nova.Network/Pcp/PcpChannelPacket.cs`
- `src/Nova.Playback/PlaybackOneEventFinalEosFlvParser.cs`
- `src/Nova.Playback/PlaybackFiniteOneEventPacketSubmissionStateMachine.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9i — Finite Awaited PCP-to-FLV Composition Boundary**

`IPcpChannelPacketConsumer`を実装するserialized boundaryを追加し、callback内local suffix offset、injected generation factory、bounded parser／decoder transitions、別操作の明示finalization、discontinuity generation replacement、fixed terminal snapshot、ordered cleanupをcross-platform focused testで実装・検証します。production backend activation、decoded-frame delivery、output wiringは追加しません。

## 未完了

- finite awaited PCP-to-FLV composition implementation
- bounded decode iteration and per-stream EOS drain
- decoded-frame ownership and delivery for the real FLV path
- MP4／fragmented MP4 parser
- production decoder ingress
- discontinuity-time native reset semantics
- duration cutoff
- complete A/V synchronization
- runtime distribution decision
- controlled audible／visible playback
- GUI、installer、recorder
