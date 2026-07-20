# PeerCast Nova Navigation

## 現在地

**0.0.9d — Metadata-aware Native Packet and Flush Boundary — Implemented**

0.0.9cで承認したexact FFmpeg 8 ABI境界に従い、source-owned FLV packet／per-stream EOSをopened native decoder sessionへ有限に渡す境界を実装しました。

- exact source identity、stream ID、media kind、codec role、time baseをnative work前に検証
- PTS／DTS／durationをimplicit rescaleなしでintegral mapping
- unknown PTS／DTSは`AV_NOPTS_VALUE`、unknown durationは`0`
- video keyframeだけを`AV_PKT_FLAG_KEY`へmapping
- FFmpeg-owned packetと64-byte padding付きpayloadを確保
- reviewed FFmpeg 8 x64 metadata prefixだけを書き込み
- 1操作につきpacket sendまたはnull flushを1回だけ実行
- packet `EAGAIN`ではnative packet＋active parser completionを保持
- flush `EAGAIN`ではper-stream EOS completionを保持
- first-stream EOSの`PacketsAvailable`とfinal-stream EOSの`EndOfStream`を区別
- cleanupはnative packetを先に、parser completionを後に解放
- receive loop、hidden retry、second queue、PCP composition、backend activationは未追加

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `src/Nova.Playback/PlaybackMetadataAwareNativePacketBoundary.cs`
- `tests/Nova.Playback.Tests/PlaybackMetadataAwareNativePacketBoundaryTests.cs`
- `docs/adr/ADR-0114-metadata-aware-native-packet-and-flush-boundary.md`
- `docs/reviews/PCN-0122-METADATA-AWARE-NATIVE-PACKET-AND-FLUSH-BOUNDARY-VALIDATION.md`
- `docs/adr/ADR-0113-avpacket-metadata-and-flush-abi-review.md`
- `src/Nova.Playback/PlaybackBoundedFlvNativeDecoderSession.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9e — Finite One-event Packet Submission State Machine**

1つのactive parser completionを保持したまま、packet send／null flush、最大1回のbounded frame receive progress、retained packet／flush retry、success／EOF／output-required ownership transitionを明示する有限state machineを実装します。

## 未完了

- finite one-event packet submission state machine
- controlled retained FLV fixture validation
- PCP-to-parser composition
- bounded decode iteration and per-stream EOS drain
- MP4／fragmented MP4 parser
- production decoder ingress
- discontinuity reset semantics
- duration cutoff
- complete A/V synchronization
- runtime distribution decision
- controlled audible／visible playback
- GUI、installer、recorder
