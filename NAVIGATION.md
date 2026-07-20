# PeerCast Nova Navigation

## 現在地

**0.0.9a — Timestamp-aware Packet Submission Architecture Review — Accepted**

0.0.8x demux completionからopened decoderへ進むためのownership／ABI順序をreviewしました。

- 0.0.8z bindingはexact source identityとH.264／AAC parametersを持つがnative contextsは未作成
- old diagnostic native leaseはsource session／stream IDs／time basesを保持しないためreal FLV compositionには使わない
- 0.0.9bでsource-bound opened native decoder sessionを実装
- video context create→parameter transfer→openの後にaudioを同じ順序で処理
- bindingとvideo／audio gate leasesをsuccess／failureに関係なくconsume
- video／audio contextはdistinct gate／runtime ownership chainを持つ
- current packet boundaryはpayload-onlyでPTS／DTS／duration／time base／keyframe／sequenceを保持できない
- AVPacket field writeはmajor-version gateだけでは承認せず0.0.9c ABI reviewへ分離
- `NeedFrameDrain`中はactive parser completionを唯一のpending ownerとして保持
- EOSはempty payloadではなくexplicit null-packet flush＋bounded drain
- second encoded-packet queue、hidden retry loop、resident decode loopは追加しない

runtime source、tests、native ABI、packet submission、PCP consumer、backend activationは0.0.9aでは変更していません。

## CI

通常のGitHub ActionsはUbuntuのみです。同一PRの古いrunは自動キャンセルします。失敗時だけbuild log artifactを1日保持します。最終branch headでrestore、Release build、full tests、formatが成功してからmergeします。

## 最新の入口

- `docs/adr/ADR-0111-timestamp-aware-packet-submission-architecture-review.md`
- `docs/reviews/PCN-0119-TIMESTAMP-AWARE-PACKET-SUBMISSION-ARCHITECTURE-REVIEW.md`
- `src/Nova.Playback/PlaybackBoundedFlvDecoderParameterBinding.cs`
- `src/Nova.Playback/PlaybackDiagnosticNativeParameterTransaction.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegContextLease.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegPacketFrameBoundary.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegAbiSymbolGate.cs`

## 次の作業

**0.0.9b — Source-bound Opened Native Decoder Session**

liveな0.0.8z bindingとdistinct video／audio gate leasesをconsumeし、exact source-session reference、stream IDs、time basesと、parameters transferred＋opened状態のH.264／AAC contextsを1個のowned sessionへまとめます。

## 未完了

- source-bound opened native decoder session
- AVPacket metadata／flush ABI review
- metadata-aware packet boundary
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
