# PeerCast Nova Navigation

## 現在地

**0.0.8x — Pure Managed Bounded FLV v1 Demux Parser — Implemented**

0.0.8vのcontainer-neutral contract上に、最初のreal container parserをpure managedで実装しました。

- input: caller-owned `ReadOnlyMemory<byte>` chunkを有限push
- progress: consumed byte count＋最大1個の0.0.8v completion
- carry: splitしたFLV header／tag／previous-tag-sizeの1 recordだけを有限保持
- framing: signature、version、flags、data offset、tag type／size／stream ID、PreviousTagSizeをstrict validation
- bootstrap: AVC sequence headerの後にAAC sequence headerを要求し、media前にimmutable sessionを作成
- metadata: script tagはcount／byte bound内でskip。AMFは解析しない
- AVC config: avcC reserved bits、NAL length size、SPS／PPS count・type・length・profile／levelを検証
- SPS: emulation-prevention除去、profile syntax、scaling list、POC、interlace、croppingから寸法を取得
- video packet: AVCC length walk、FLV timestamp=DTS、signed composition offsetでPTS、frame typeでkeyframe
- AAC config: AAC-LC、indexed／explicit sample rate、channel configurationを有限解析
- audio packet: FLV AAC headerを除いたpayload、PTS=DTS、duration unknown
- duplicate config: byte一致のみbounded skip。変更はfail closed
- EOS: AVC end-of-sequenceとfinal inputをper-stream EOSへ対応
- backpressure: active completionをdisposeするまで次Parseを拒否
- discontinuity: old session／carry／bootstrap／sequence／EOSをresetしてgenerationを更新
- cleanup: active completion→session→carry／temporary configurationの順でidempotent cleanup

PCP consumer、decoder codec-parameter transfer、packet submission、decode loop、FFmpeg backend activation、Windows device activationは未接続です。

## CI

通常のGitHub ActionsはUbuntuのみです。同一PRの古いrunは自動キャンセルします。失敗時だけbuild log artifactを1日保持します。最終branch headでrestore、Release build、full tests、formatが成功してからmergeします。

## 最新の入口

- `src/Nova.Playback/PlaybackBoundedFlvParser.cs`
- `tests/Nova.Playback.Tests/PlaybackBoundedFlvParserTests.cs`
- `docs/adr/ADR-0108-pure-managed-bounded-flv-parser.md`
- `docs/reviews/PCN-0116-PURE-MANAGED-BOUNDED-FLV-PARSER-VALIDATION.md`
- `src/Nova.Playback/PlaybackBoundedDemuxOutputContract.cs`
- `docs/adr/ADR-0107-first-bounded-container-parser-selection.md`

## 次の作業

0.0.8x parserから既存decoder境界へ進む前に、**parser-to-decoder composition architecture review**を行います。

- immutable stream descriptionを既存managed codec-parameter ownerへどう渡すか
- 0.0.8v completionをdisposeする前にpacket submissionをどこまで有限化するか
- PTS／DTS、EOS、partial failure、native lease、cancellationの所有権
- parser backpressureを壊さず、decoder側に第2queueを作らないcomposition seam
- production activationとPCP adapterを引き続き分離すること

## 未完了

- parser-to-decoder composition review and implementation
- controlled retained FLV fixture validation
- PCP-to-parser composition
- MP4／fragmented MP4 parser
- production decoder ingress
- continuous decode、flush、EOS drain
- duration cutoff
- complete A/V synchronization
- FFmpeg activation／distribution
- controlled audible／visible playback
- GUI、installer、recorder
