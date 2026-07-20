# PeerCast Nova Navigation

## 現在地

**0.0.8y — Parser-to-Decoder Composition Architecture Review — Accepted**

0.0.8x parserからdecoder境界への接続方法をreviewしました。

- FLV session descriptionは既存managed codec-parameter factoryへ安全に写像可能
- 0.0.8zでsource session参照とvideo／audio parameter leasesをまとめるpure managed bindingを実装
- bindingはparserやsessionを所有しない
- current packet boundaryはpayload bytesのみでPTS／DTS／time base／keyframe／sequenceを保持できない
- existing diagnostic transactionはvideo→audio固定pairで、FLVのinterleaved orderと一致しない
- decoderがframe drainを要求した場合はactive parser completionを保持する必要がある
- EOSはexplicit flushとbounded drainを別境界で扱う
- timestamp-aware packet submissionは後続reviewへ分離

PCP consumer、packet submission、decode loop、backend activation、Windows device activationは未接続です。

## CI

通常のGitHub ActionsはUbuntuのみです。同一PRの古いrunは自動キャンセルします。失敗時だけbuild log artifactを1日保持します。最終branch headでrestore、Release build、full tests、formatが成功してからmergeします。

## 最新の入口

- `docs/adr/ADR-0109-parser-decoder-composition-review.md`
- `docs/reviews/PCN-0117-PARSER-DECODER-COMPOSITION-REVIEW.md`
- `src/Nova.Playback/PlaybackBoundedFlvParser.cs`
- `src/Nova.Playback/PlaybackBoundedDemuxOutputContract.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegCodecParameters.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegPacketFrameBoundary.cs`

## 次の作業

**0.0.8z — FLV Session Decoder-Parameter Binding**

liveなFLV demux sessionを検証し、source session参照、stream IDs、time basesと、既存factoryが所有するH.264／AAC codec-parameter leasesをpure managed bindingへまとめます。

## 未完了

- FLV session decoder-parameter binding
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
