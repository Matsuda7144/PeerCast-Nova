# PeerCast Nova Navigation

## 現在地

**0.0.9f — Controlled Retained FLV Fixture Validation — Validated**

実bounded FLV bytesからparser completion、exact source session、source-bound native session identity、0.0.9e state machineまでをproduction activationなしのcontrolled fixtureで接続しました。

- video→audio→video→audioのpacket interleaveを保持
- video／audioのper-stream sequenceを独立保持
- AVCC／AAC payload、DTS、signed composition-time PTSを保持
- video keyframe／non-keyframe状態を保持
- packet `EAGAIN`で元のnative packet＋parser completionを保持
- flush `EAGAIN`で元のper-stream EOS completionを保持
- retained中のparserは`ActiveCompletion`かつ0 byte consumed
- retained transitionごとにreceive 1回＋retry 1回だけ実行
- explicit video EOS後のfinal audio EOSを独立flushとして受理
- native FFmpeg、decoded output、PCP composition、backend activationは未接続
- 通常final inputのvideo＋audio dual-EOS completionはone-event state machineがnative work前にfail closed

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `tests/Nova.Playback.Tests/PlaybackControlledRetainedFlvFixtureValidationTests.cs`
- `docs/adr/ADR-0116-controlled-retained-flv-fixture-validation.md`
- `docs/reviews/PCN-0124-CONTROLLED-RETAINED-FLV-FIXTURE-VALIDATION.md`
- `src/Nova.Playback/PlaybackFiniteOneEventPacketSubmissionStateMachine.cs`
- `src/Nova.Playback/PlaybackBoundedFlvParser.cs`
- `docs/adr/ADR-0115-finite-one-event-packet-submission-state-machine.md`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9g — One-event Final EOS Sequencing**

両streamが未終了のfinal inputでもparserが1操作につき1つのsource-owned per-stream EOS completionだけを返すようにし、video EOSのcompletion解放後にaudio EOSを返します。active-completion backpressure、sequence commit、final stateを保ち、detached EOS copyやsecond queueは追加しません。

## 未完了

- one-event final EOS sequencing
- PCP-to-parser composition
- bounded decode iteration and per-stream EOS drain
- decoded-frame ownership and delivery for the real FLV path
- MP4／fragmented MP4 parser
- production decoder ingress
- discontinuity reset semantics
- duration cutoff
- complete A/V synchronization
- runtime distribution decision
- controlled audible／visible playback
- GUI、installer、recorder
