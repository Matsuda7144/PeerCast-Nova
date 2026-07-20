# PeerCast Nova Navigation

## 現在地

**0.0.9j — Bounded Decode Iteration and Per-stream EOS Drain Review — Reviewed**

0.0.9iでacceptedになったpacket／flush transitionからdecoded frameを失わずに受け取る有限iterationと、video／audioを独立してdecoder EOFまでdrainする契約を確定しました。

- 現行production `Advance`のstate-only receiveは成功した一時`AVFrame`をreturn前に解放するため、後続のsecond receiveでは同じframeをcopyできない
- 0.0.9kではsource-bound receive boundaryをgeneration唯一のproduction receive authorityにする
- 1 public operationは最大1回だけ`avcodec_receive_frame`を呼ぶ
- callback-scoped native frameから既存managed frame copyまでを同じreceive内で完了し、native pointerを保持しない
- copied frameはexact source、generation、stream、media、independent decoded sequence、discontinuityを持つ1つのowned completionになる
- generation全体でactive decoded completionは最大1件
- retained packet／flushはdecoded frameが受理・解放された後だけ最大1回retry
- produced frameがbackpressureされた場合はdecoded ownershipとencoded submissionの両方を保持
- packet acceptance後の通常receiveはcaller-drivenで1回1frame、`NeedMoreInput`は正常な有限終了
- parser EOS、null flush acceptance、flush drain、decoder EOFを別状態として扱う
- accepted flush後はvideo／audioを別々に1回1frameでdrainし、各contextのEOFでのみstream完了
- pending-send reliefまたはflush drain中の`NeedMoreInput`はno-progress failure
- frame count／managed byte budget、cancellation、discontinuity generation replacement、decoded-frame-first cleanupを要求
- second decoded queue、resident loop、double receive、`avcodec_flush_buffers`によるEOS処理なし

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0120-bounded-decode-iteration-and-per-stream-eos-drain.md`
- `docs/reviews/PCN-0128-BOUNDED-DECODE-ITERATION-AND-PER-STREAM-EOS-DRAIN-REVIEW.md`
- `src/Nova.Playback/PlaybackFiniteOneEventPacketSubmissionStateMachine.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegPacketFrameBoundary.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegNativeFrameBoundary.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegFrameMetadata.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegFrameDataBoundary.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegManagedFrameCopy.cs`
- `src/Nova.Playback/PlaybackPcpFlvCompositionBoundary.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9k — Source-bound One-frame Receive and EOS Drain Boundary**

exact source-bound decoder generationに1回1frameのreceive／managed ownership boundaryを追加します。pending packet／flush relief、ordinary post-accept receive、video／audio independent flush drainを同じ固定contractで実装し、source／generation identity、decoded sequence、active completion backpressure、mode-specific no-progress、frame／byte budget、cancellation、failure、cleanupをcross-platform focused testで検証します。

## 未完了

- source-bound one-frame receive and per-stream EOS drain implementation
- decoded-frame conversion／scheduler ingress／delivery for the real FLV path
- production generation factory and backend activation
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- runtime distribution decision
- controlled audible／visible playback
- GUI、installer、recorder