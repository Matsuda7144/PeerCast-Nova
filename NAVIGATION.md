# PeerCast Nova Navigation

## 現在地

**0.0.9b — Source-bound Opened Native Decoder Session — Implemented**

0.0.8zのFLV decoder-parameter bindingを、exact source identity付きのopened native H.264/AAC context pairへ接続しました。

- source session、stream IDs、time basesをnative work前にsnapshot
- distinct video／audio ABI gate ownershipを要求
- H.264 create→parameter transfer→open完了後にAACへ進む
- bindingと両gateを全resultでconsume
- partial configured／opened stateをsuccessとして返さない
- source disposal、context／transfer／open rejection、faultを明示
- success sessionはopened contextsだけを所有
- source sessionはnon-owning identity reference
- cleanupはaudio→video順でidempotent
- packet submission、AVPacket metadata、flush、drain、PCP composition、backend activationは未変更

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `src/Nova.Playback/PlaybackBoundedFlvNativeDecoderSession.cs`
- `tests/Nova.Playback.Tests/PlaybackBoundedFlvNativeDecoderSessionTests.cs`
- `docs/adr/ADR-0112-source-bound-opened-native-decoder-session.md`
- `docs/reviews/PCN-0120-SOURCE-BOUND-OPENED-NATIVE-DECODER-SESSION-VALIDATION.md`
- `src/Nova.Playback/PlaybackBoundedFlvDecoderParameterBinding.cs`
- `docs/adr/ADR-0111-timestamp-aware-packet-submission-architecture-review.md`

## 次の作業

**0.0.9c — AVPacket Metadata and Flush ABI Review**

FFmpeg runtime identity、AVPacket layoutまたはverified setter seam、PTS／DTS／duration／time base／keyframe／unknown value／stream selection、null-packet flush、ABI gate strengtheningをreviewします。

## 未完了

- AVPacket metadata／flush ABI review
- metadata-aware native packet boundary
- finite one-event submission and EOS drain state
- controlled retained FLV fixture validation
- PCP-to-parser composition
- MP4／fragmented MP4 parser
- production decoder ingress
- duration cutoff
- complete A/V synchronization
- runtime distribution decision
- controlled audible／visible playback
- GUI、installer、recorder
