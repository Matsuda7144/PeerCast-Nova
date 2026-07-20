# PeerCast Nova Navigation

## 現在地

**0.0.8w — First Bounded Container Parser Selection Review — Accepted**

0.0.8v contractの最初のconcrete parserとして、pure managed bounded FLV v1 demux parserを選択しました。

- selection: FLVを先行し、MP4／fragmented MP4は後続reviewへ延期
- input: seekable fileではなくcaller-owned callback chunkを有限push
- progress: consumed byte count＋最大1個の0.0.8v completion
- carry: chunkをまたぐ不完全なheader／tag／previous-tag-sizeだけを有限保持
- no queue: complete packet、複数tag、caller suffix、output event queueを内部保持しない
- bootstrap: FLV header、PreviousTagSize0、AVC sequence header、AAC sequence headerをmedia前に要求
- metadata: script tagはbounded skipのみ。AMFからcodec configurationを合成しない
- AVC: AVCDecoderConfigurationRecord、bounded SPS dimension parse、AVCC NAL length walk
- video timing: FLV timestampをDTS、signed composition offsetを加えた値をPTS
- AAC: AudioSpecificConfigからAAC-LC sample rate／channel countを有限解析
- EOS: AVC end-of-sequenceとfinal inputを明示EOSへ対応し、flush／drain packetは作らない
- discontinuity: carryとparser stateをclearし、新しいFLV header／sequence headersを要求
- commit: 0.0.8v completion構築成功後にだけparser stateを進める

FLV parser runtime、PCP-to-parser composition、decoder packet submission、FFmpeg backend activation、Windows device activationは未接続のままです。

## CI

通常のGitHub ActionsはUbuntuのみです。同一PRの古いrunは自動キャンセルします。失敗時だけbuild log artifactを1日保持します。最終branch headでrestore、Release build、full tests、formatが成功してからmergeします。

## 最新の入口

- `docs/adr/ADR-0107-first-bounded-container-parser-selection.md`
- `docs/reviews/PCN-0115-FIRST-BOUNDED-CONTAINER-PARSER-SELECTION-REVIEW.md`
- `src/Nova.Playback/PlaybackBoundedDemuxOutputContract.cs`
- `tests/Nova.Playback.Tests/PlaybackBoundedDemuxOutputContractTests.cs`
- `src/Nova.Network/Pcp/PcpChannelPacket.cs`
- `src/Nova.Network/Pcp/IPcpChannelPacketConsumer.cs`
- `src/Nova.Network/Pcp/PcpRelayFanout.cs`

## 次の作業

**0.0.8x — Pure Managed Bounded FLV v1 Demux Parser**

caller-owned chunk、consumed-byte progress、one incomplete-record carry、FLV structural validation、AVC／AAC sequence-header bootstrap、bounded SPS／AudioSpecificConfig parsing、packet／EOS output、discontinuity resetをpure managed runtimeとfocused testsで実装します。

PCP consumerへの接続、decoder codec-parameter transfer、packet submission、decode loop、production activationは含めません。

## 未完了

- pure managed bounded FLV v1 parser implementation
- real FLV fixture validation
- PCP-to-parser composition
- parser-to-codec-parameter composition
- MP4／fragmented MP4 parser
- production decoder ingress
- continuous decode、flush、EOS drain
- duration cutoff
- complete A/V synchronization
- FFmpeg activation／distribution
- controlled audible／visible playback
- GUI、installer、recorder
