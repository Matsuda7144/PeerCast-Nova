# PeerCast Nova Navigation

## 現在地

**0.0.8v — Pure Managed Bounded Demux Output Contract — Implemented**

container parserより先に、container-neutralなbounded demux output contractをpure managedで実装しました。

- session: H.264 video 1本＋AAC audio 1本のimmutable owned description
- stream: stable distinct id、bounded codec data、video／audio configuration、positive rational time base
- container: FLV／MP4／fragmented MP4をfixed enumで表現
- timing: PTSとDTSを独立したknown／unknown値として保持し、negative timestampもbound内で維持
- converter: rational time baseをchecked conversionし、invalid base／limit／magnitudeを明示
- packet: parser order、per-stream monotonic sequence、owned bounded payload、duration、keyframe、discontinuity
- EOS: streamごとのpayloadなし明示event。両stream終了後だけfinal EOS
- bounds: input bytes、packet bytes、codec data、packet count、aggregate owned bytes、skipped records、timestamp magnitude
- backpressure: active completionをdisposeするまで次のfinite readを`ActiveCompletion`で拒否
- cleanup: packet payloadを先にclearし、その後audio／video codec dataをclear
- fail closed: sequence regression、stream mismatch、packet after EOS、contradictory result、undefined enum

FLV／MP4 parser、decoder packet submission、decode loop、FFmpeg backend activation、Windows device activationは未接続のままです。

## CI

通常のGitHub ActionsはUbuntuのみです。同一PRの古いrunは自動キャンセルします。失敗時だけbuild log artifactを1日保持します。最終branch headでrestore、Release build、full tests、formatが成功してからmergeします。

## 最新の入口

- `src/Nova.Playback/PlaybackBoundedDemuxOutputContract.cs`
- `tests/Nova.Playback.Tests/PlaybackBoundedDemuxOutputContractTests.cs`
- `docs/adr/ADR-0106-pure-managed-bounded-demux-output-contract.md`
- `docs/reviews/PCN-0114-PURE-MANAGED-BOUNDED-DEMUX-OUTPUT-VALIDATION.md`
- `docs/adr/ADR-0105-bounded-demux-output-contract-review.md`
- `src/Nova.Playback/PlaybackDecoderFfmpegPacketFrameBoundary.cs`

## 次の作業

0.0.8v contractの上に最初のconcrete bounded container parserを置く前に、FLVとMP4／fragmented MP4のどちらを先に選ぶかarchitecture reviewで決定します。

選定では、PeerCast／HTTP relayで実際に必要なcontainer、non-seekable stream対応、codec configuration、timestamp／DTS、keyframe、EOS、input observation boundsを比較し、production activationとは分離します。

## 未完了

- first bounded container parser selection and implementation
- bounded FLV／MP4 parser
- parser-to-codec-parameter composition
- production decoder ingress
- continuous decode、flush、EOS drain
- duration cutoff
- complete A/V synchronization
- FFmpeg activation／distribution
- controlled audible／visible playback
- GUI、installer、recorder
