# PeerCast Nova Navigation

## 現在地

**0.0.9c — AVPacket Metadata and Flush ABI Review — Accepted**

0.0.9bのsource-bound opened native H.264/AAC context pairに対して、次のpacket／flush implementationが従うABI境界を確定しました。

- exact verified FFmpeg 8 runtime identityだけを承認
- major-version-onlyまたはarbitrary runtimeのdirect field writeを拒否
- packet storageは`av_packet_alloc`／`av_packet_free`で所有
- managed `sizeof(AVPacket)` allocationは禁止
- reviewed prefixはPTS、DTS、data、size、stream index、key flag、duration、time baseだけ
- unknown PTS／DTSは`AV_NOPTS_VALUE`
- unknown durationは`0`
- exact source stream IDとsnapshotted time baseを要求
- EOS flushは`avcodec_send_packet(context, NULL)`
- `avcodec_flush_buffers`はEOS drainに使用しない
- sendは1回だけ、`EAGAIN`ではactive parser completionとnative packet ownershipを保持
- second encoded queue、sleep、spin、hidden retry／drain loopは禁止
- runtime source、packet submission、flush execution、drain、PCP composition、backend activationは未変更

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0113-avpacket-metadata-and-flush-abi-review.md`
- `docs/reviews/PCN-0121-AVPACKET-METADATA-AND-FLUSH-ABI-REVIEW.md`
- `src/Nova.Playback/PlaybackBoundedFlvNativeDecoderSession.cs`
- `docs/adr/ADR-0112-source-bound-opened-native-decoder-session.md`
- `docs/reviews/PCN-0120-SOURCE-BOUND-OPENED-NATIVE-DECODER-SESSION-VALIDATION.md`
- `docs/adr/ADR-0111-timestamp-aware-packet-submission-architecture-review.md`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9d — Metadata-aware Native Packet and Flush Boundary**

exact source／stream／time-base validation、FFmpeg-owned padded packet allocation、metadata mapping、1回のsendまたはnull flush、`EAGAIN` ownership retention、packet-first cleanupを有限なnative boundaryとして実装します。

## 未完了

- metadata-aware native packet／flush boundary
- finite one-event submission and EOS drain state
- controlled retained FLV fixture validation
- PCP-to-parser composition
- MP4／fragmented MP4 parser
- production decoder ingress
- discontinuity reset semantics
- duration cutoff
- complete A/V synchronization
- runtime distribution decision
- controlled audible／visible playback
- GUI、installer、recorder
