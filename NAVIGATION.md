# PeerCast Nova Navigation

## 現在地

**0.0.8u — Bounded Demux Output Contract Review — Accepted**

0.0.8dから0.0.8tまでの有限診断経路を再確認し、production compositionより先にcontainer-neutralなbounded demux output contractを置く方針を固定しました。

- 現状: 一般FLV／MP4 demux contractとparserは未実装
- diagnostic fixture: H.264 packet 1個＋AAC packet 1個だけで、real containerの代替にはしない
- stream description: H.264 video 1本＋AAC audio 1本、stable stream id、codec data、time base
- packet: stream id、media kind、sequence、owned payload、PTS／DTS、duration、keyframe、discontinuity
- timing: PTSとDTSを分離し、unknownをzeroへ変換しない
- EOS: payloadなしの明示event
- bounds: input、packet count、packet bytes、aggregate owned bytes、codec data、skipped records、timestamp conversion
- backpressure: caller-driven finite read。resident readerとparser-owned producer queueなし
- ownership: success completionがpayload／codec dataを所有し、disposeで全解放
- parser order: pure managed contract → one bounded container parser → decoder composition review

FFmpeg backend activation、Windows device activation、production decoder ingressは未承認・未接続のままです。

## CI

通常のGitHub ActionsはUbuntuのみです。同一PRの古いrunは自動キャンセルします。失敗時だけbuild log artifactを1日保持します。最終branch headでrestore、Release build、full tests、formatが成功してからmergeします。

## 最新の入口

- `src/Nova.Playback/PlaybackDiagnosticLateAwareSchedulerDispatchTransaction.cs`
- `src/Nova.Playback/BoundedDiagnosticPlaybackFixture.cs`
- `src/Nova.Playback/PlaybackDiagnosticFixtureAdapter.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegPacketFrameBoundary.cs`
- `docs/adr/ADR-0105-bounded-demux-output-contract-review.md`
- `docs/reviews/PCN-0113-BOUNDED-DEMUX-OUTPUT-CONTRACT-REVIEW.md`

## 次の作業

**0.0.8v — Pure Managed Bounded Demux Output Contract**

container parserやnative callを追加せず、stream description、packet／EOS event、PTS／DTS、keyframe、discontinuity、count／byte bounds、ownership、finite-read backpressureをpure managed contractとfocused testsで実装します。

## 未完了

- bounded demux output contract implementation
- bounded FLV／MP4 parser
- production decoder ingress
- continuous decode、flush、EOS drain
- duration cutoff
- complete A/V synchronization
- FFmpeg activation／distribution
- controlled audible／visible playback
- GUI、installer、recorder
