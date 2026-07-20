# PeerCast Nova Navigation

## 現在地

**0.0.9e — Finite One-event Packet Submission State Machine — Implemented**

0.0.9dのmetadata-aware packet／flush completionを1つだけ保持し、初回sendからbounded receive progressとretained retryまでを有限state machineとして実装しました。

- `Begin`はpacket sendまたはnull flushを1回だけ実行
- `EAGAIN`ではnative packet／flush状態とactive parser completionをそのまま保持
- `Advance`はframe receiveを最大1回だけ実行
- 明示的なframe progress後だけretained packet／flushを最大1回再試行
- repeated `EAGAIN`でもactive submissionは1つだけでsecond encoded copyなし
- per-stream sequenceはnative acceptance後だけcommit
- video／audioのflush acceptedとdecoder EOFを独立管理
- accepted flush／EOF後の同一stream inputをnative work前に拒否
- source mismatch、non-increasing sequence、concurrent inputをfail closed
- cancellation／failure／disposeはnative packetを先に、parser completionを後に解放
- source sessionとnative decoder sessionは非所有
- resident loop、decoded-frame delivery、PCP composition、backend activationは未追加

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `src/Nova.Playback/PlaybackFiniteOneEventPacketSubmissionStateMachine.cs`
- `tests/Nova.Playback.Tests/PlaybackFiniteOneEventPacketSubmissionStateMachineTests.cs`
- `docs/adr/ADR-0115-finite-one-event-packet-submission-state-machine.md`
- `docs/reviews/PCN-0123-FINITE-ONE-EVENT-PACKET-SUBMISSION-STATE-MACHINE-VALIDATION.md`
- `src/Nova.Playback/PlaybackMetadataAwareNativePacketBoundary.cs`
- `docs/adr/ADR-0114-metadata-aware-native-packet-and-flush-boundary.md`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9f — Controlled Retained FLV Fixture Validation**

bounded FLV parserの実completionをsource-bound native sessionと0.0.9e state machineへ接続するcontrolled fixtureを追加し、packet order、PTS／DTS、keyframe、audio／video interleave、parser backpressure、retained retry、per-stream EOSをproduction activationなしで検証します。

## 未完了

- controlled retained FLV fixture validation
- PCP-to-parser composition
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
