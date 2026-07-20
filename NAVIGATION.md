# PeerCast Nova Navigation

## 現在地

**0.0.9k — Source-bound One-frame Receive and EOS Drain Boundary — Implemented**

exact FLV sourceとopened native decoder sessionを1世代として所有し、1回のnative receiveを失わず1つのmanaged decoded completionへ移す有限boundaryを実装しました。

- `PlaybackSourceBoundOneFrameDecodeBoundary`がexact source、decoder generation、native session、専用submission state machineを所有
- 1 public receive operationは最大1回のcallback-scoped native receive／managed copy
- state-only receive後のsecond receiveを使わず、成功frameを同じcallback内でcopy
- completionはsource、decoder／discontinuity generation、stream、media、independent decoded sequence、managed bytesを保持
- generation全体でactive decoded completionは最大1件
- ordinary、pending submission relief、flush drainを固定modeとして分離
- retained packet／flushはrelief frame completion解放後の別操作で最大1回だけretry
- repeated `EAGAIN`は次の1-frame receiveと次の1-retryを要求
- ordinary `NeedMoreInput`は正常、relief／flush drain中はno-progress failure
- accepted video／audio flushを独立して1回1frameでdecoder EOFまでdrain
- reusable frame-count／managed-byte budget、cancellation、fixed snapshot、decoded-frame-first cleanup
- source sessionは非所有、native sessionはaudio→video順で解放
- second decoded queue、resident loop、production activation、scheduler／output wiringなし

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `src/Nova.Playback/PlaybackSourceBoundOneFrameDecodeBoundary.cs`
- `tests/Nova.Playback.Tests/PlaybackSourceBoundOneFrameDecodeBoundaryTests.cs`
- `tests/Nova.Playback.Tests/PlaybackSourceBoundOneFrameDecodeBackpressureTests.cs`
- `tests/Nova.Playback.Tests/PlaybackSourceBoundOneFrameDecodeTestSupport.cs`
- `docs/adr/ADR-0121-source-bound-one-frame-receive-and-eos-drain.md`
- `docs/reviews/PCN-0129-SOURCE-BOUND-ONE-FRAME-RECEIVE-AND-EOS-DRAIN-VALIDATION.md`
- `src/Nova.Playback/PlaybackPcpFlvCompositionBoundary.cs`
- `src/Nova.Playback/PlaybackFiniteOneEventPacketSubmissionStateMachine.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9l — Awaited Decoded Completion Composition Review**

0.0.9iのawaited PCP-to-FLV composition generationを0.0.9kの唯一のreceive authorityへ置き換える接続をレビューします。callback内で必要なretained-send relief、callback外のordinary receive／flush drain駆動、decoded completion consumer backpressure、conversion／bounded scheduler ingressへの所有権移譲、discontinuity replacement、budget共有、停止・cleanup順を先に確定し、runtime composition実装は次のmilestoneへ分離します。

## 未完了

- 0.0.9k boundaryとawaited PCP-to-FLV generationのcomposition
- real FLV decoded completionのconversion／scheduler ingress／delivery
- production generation factory and backend activation
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- runtime distribution decision
- controlled audible／visible playback
- GUI、installer、recorder
