# PeerCast Nova Navigation

## 現在地

**0.0.8z — FLV Session Decoder-Parameter Binding — Implemented**

0.0.8xのbounded FLV sessionを、既存managed codec-parameter ownership境界へpure managedで接続しました。

- source: liveなFLV demux sessionだけを受理
- identity: exact source-session referenceをnon-owningで保持
- stream: video 0 H.264／audio 1 AACを厳密検証
- time base: video／audioともにexact `1/1000`
- video parameters: parser由来width／height／avcCを既存factoryへ渡す
- audio parameters: parser由来sample rate／channel count／AudioSpecificConfigを既存factoryへ渡す
- ownership: independently copied padded video／audio parameter leasesだけをbindingが所有
- ordering: video作成後にaudioを作成
- partial failure: audio失敗時はvideo leaseを破棄
- source lifetime: bindingはparser／sessionを所有・破棄しない
- cleanup: audio→video順のidempotent disposal
- independence: binding成功後にsource sessionが破棄されてもparameter copiesは有効

packet completion、native context、parameter transfer、codec open、packet submission、PCP consumer、backend activationは未接続です。

## CI

通常のGitHub ActionsはUbuntuのみです。同一PRの古いrunは自動キャンセルします。失敗時だけbuild log artifactを1日保持します。最終branch headでrestore、Release build、full tests、formatが成功してからmergeします。

## 最新の入口

- `src/Nova.Playback/PlaybackBoundedFlvDecoderParameterBinding.cs`
- `tests/Nova.Playback.Tests/PlaybackBoundedFlvDecoderParameterBindingTests.cs`
- `docs/adr/ADR-0110-flv-session-decoder-parameter-binding.md`
- `docs/reviews/PCN-0118-FLV-SESSION-DECODER-PARAMETER-BINDING-VALIDATION.md`
- `docs/adr/ADR-0109-parser-decoder-composition-review.md`
- `src/Nova.Playback/PlaybackBoundedFlvParser.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegCodecParameters.cs`

## 次の作業

**Timestamp-aware Packet Submission Architecture Review**

0.0.8vの1 event completionをexact source bindingへ照合し、parser order、PTS／DTS／time base、keyframe、sequence、decoder backpressure、EOS flush／drainを情報損失なく扱う次の有限境界を決定します。

## 未完了

- timestamp-aware packet submission review and implementation
- controlled retained FLV fixture validation
- PCP-to-parser composition
- MP4／fragmented MP4 parser
- production decoder ingress
- continuous decode、flush、EOS drain
- duration cutoff
- complete A/V synchronization
- runtime distribution decision
- controlled audible／visible playback
- GUI、installer、recorder
