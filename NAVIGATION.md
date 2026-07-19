# PeerCast Nova Navigation

## 現在地

**Milestone 0.0.8p — Playback Cutover Architecture Review**

- 0.0.8k〜0.0.8oの有限診断経路: 実装・CI検証済み
- playback cutover architecture review: 完了
- FFmpeg backend activation: 未承認
- FLV/MP4 demux、continuous decode、実機再生: 未完成
- current: H.264/AAC各1packetを独立native contextへ送り、callback-scoped native frame、metadata、plane/sample view、managed copy、managed conversion、awaited sink deliveryまでを診断専用の有限transactionとして接続済み
- cutover decision: converted callback payloadではなく、conversion前のowned managed frameを既存`BoundedPlaybackScheduler`へ渡す
- prerequisite: schedulerはframe数だけでなくaggregate owned-byte budgetを持つまでdecoder ingressへ接続しない
- safety: `PlaybackDecoderBackendFactory`等から診断経路へのactivation参照はなく、未承認状態を維持
- distribution: ユーザー/ホスト導入、動的リンク、NovaによるFFmpeg同梱・再配布なし
- asset policy: 第三者media、logo、icon、font等はsource/license/redistribution permissionを記録せず追加しない

## 進捗記録の規律

- ADR-0082は採番ミスによる永久欠番。履歴・現行ツリー・関連PRに予約または破棄の記録はなく、ADR-0081の次が最初からADR-0083として作成された。
- PCN検証文書は既存規則に従い、今後も `docs/reviews/` に配置する。
- milestone実装PRには、同じPR内で `CHANGELOG.md` と本ファイルの現在地・索引更新を含める。
- activation、実機再生、法務承認など未完了事項は、実装済み境界と混同せず明示する。
- 診断統合ハーネスは本番経路から独立し、decoder backendのactivation状態を変更しない。
- 本棚卸しで `README.md`、`ROADMAP.md`、`CHANGELOG.md`、本ファイルを0.0.8pまで照合する。

## 実装済みマイルストーン

| Milestone | 内容 | 状態 |
| --- | --- | --- |
| 0.0.2c | PCP Compatibility Probe | Verified |
| 0.0.3 | Channel Session | Verified |
| 0.0.4 | Relay | Verified |
| 0.0.5a | Bounded Playback Input | Verified |
| 0.0.5b | Media Signature Classification | Verified |
| 0.0.5c | Replaceable Decoder Lifecycle | Verified |
| 0.0.5d | Decoder Lifecycle Probe | Verified |
| 0.0.5e | Decoded Output Contract | Verified |
| 0.0.5f | Decoder Backend Boundary | Verified |
| 0.0.5g | Backend Selection Probe | Verified |
| 0.0.5h | Backend Conformance Gate | Verified |
| 0.0.5i | Backend Provenance Policy | Verified |
| 0.0.5j | Backend Activation Audit | Verified |
| 0.0.5k | Backend Artifact Manifest | Verified |
| 0.0.6a | External Artifact Verification | Verified |
| 0.0.6b | Pinned FFmpeg Candidate | Technically verified; activation pending |
| 0.0.6c | Installed FFmpeg Runtime Inventory | Verified; activation pending |
| 0.0.6c1 | Explicit Installed Runtime Locator | Verified; activation pending |
| 0.0.6c2 | Verified FFmpeg Runtime Lease | Verified; activation pending |
| 0.0.6c2-fix1 | Runtime Lease CI and Artifact Consistency | Verified |
| 0.0.6d | FFmpeg ABI / Symbol Gate | Implemented; activation pending |
| 0.0.6e | Native Decoder Context Lease | Implemented; activation pending |
| 0.0.6f | Codec Parameter Ownership Boundary | Implemented; activation pending |
| 0.0.6g | Native Parameter Transfer Boundary | Implemented; activation pending |
| 0.0.6h | avcodec_open2 Boundary | Implemented; activation pending |
| 0.0.6i | Packet / Frame Processing Boundary | Implemented; activation pending |
| 0.0.6j | Callback-Scoped Native Frame Boundary | Implemented; activation pending |
| 0.0.6k | Native Frame Metadata Extraction | Implemented; activation pending |
| 0.0.6l | Frame Plane / Sample Boundary | Implemented; activation pending |
| 0.0.6m | Managed Frame Copy | Implemented; activation pending |
| 0.0.6n | Decoded Output Adapter | Implemented; activation pending |
| 0.0.6o | Managed Format Conversion | Implemented; activation pending |
| 0.0.6p | Awaited Decoded Sink Delivery | Implemented |
| 0.0.6q | Playback Policy / Clock Boundary | Implemented |
| 0.0.6r | Bounded Playback Scheduler | Implemented |
| 0.0.6s | Renderer / Audio Sink Boundary | Implemented; OS adapters pending |
| 0.0.6t | Windows Output Adapter Selection | Implemented; activation pending |
| 0.0.6u | Windows Output Activation Boundary | Implemented; activation pending |
| 0.0.6v | Windows Output Resource Binding Boundary | Implemented; activation pending |
| 0.0.6w | Windows Output Stream Initialization Boundary | Implemented; activation pending |
| 0.0.6x | Windows Output Buffer Processing Boundary | Implemented |
| 0.0.6y | Windows Output Scheduler Adapter Boundary | Implemented |
| 0.0.6z | Finite Continuous Playback Pump Policy Boundary | Implemented |
| 0.0.7a | A/V Timing Decision Boundary | Implemented |
| 0.0.7b | Timed Scheduler Dispatch Boundary | Implemented |
| 0.0.7c | Late Item Disposition Policy Boundary | Implemented |
| 0.0.7d | Late Dispatch Coordination Boundary | Implemented |
| 0.0.7e | Late Reservation Disposition Boundary | Implemented |
| 0.0.7f | Late-aware Scheduler Decision Boundary | Implemented |
| 0.0.7g | Late-aware Timed Dispatch Boundary | Implemented |
| 0.0.7h | Late-aware Continuous Pump Boundary | Implemented |
| 0.0.8a | Audio Recovery Boundary | Implemented |
| 0.0.8b | Recovery-aware Pump Orchestrator | Implemented |
| 0.0.8c | Deadline-aware Run Budget Policy | Implemented |
| 0.0.8d | Diagnostic Playback Pipeline Integration Probe Gate | Implemented; media run blocked |
| 0.0.8e | Bounded Diagnostic Playback Fixture | Verified |
| 0.0.8f | Diagnostic Fixture Adapter | Implemented; native decode blocked |
| 0.0.8g | Diagnostic Decoder Configuration | Implemented; native transfer blocked |
| 0.0.8h | Diagnostic Native Parameter Transaction | Implemented; codec open blocked |
| 0.0.8i | Diagnostic Codec Open Transaction | Implemented; packet/frame processing blocked |
| 0.0.8j | Diagnostic Packet / Frame Transaction | Implemented |
| 0.0.8k | Diagnostic Frame Metadata Transaction | Implemented |
| 0.0.8l | Diagnostic Frame Data Transaction | Implemented |
| 0.0.8m | Diagnostic Managed Frame Copy Transaction | Implemented |
| 0.0.8n | Diagnostic Managed Format Conversion Transaction | Implemented |
| 0.0.8o | Diagnostic Awaited Sink Delivery Transaction | Verified |
| 0.0.8p | Playback Cutover Architecture Review | Accepted; documentation alignment |

## 最新のコード入口

- `src/Nova.Playback/PlaybackPipelineIntegrationProbe.cs`
- `src/Nova.Playback/PlaybackDiagnosticNativeParameterTransaction.cs`
- `src/Nova.Playback/PlaybackDiagnosticCodecOpenTransaction.cs`
- `src/Nova.Playback/PlaybackDiagnosticPacketFrameTransaction.cs`
- `src/Nova.Playback/PlaybackDiagnosticFrameMetadataTransaction.cs`
- `src/Nova.Playback/PlaybackDiagnosticFrameDataTransaction.cs`
- `src/Nova.Playback/PlaybackDiagnosticManagedFrameCopyTransaction.cs`
- `src/Nova.Playback/PlaybackDiagnosticManagedFormatConversionTransaction.cs`
- `src/Nova.Playback/PlaybackDiagnosticAwaitedSinkDeliveryTransaction.cs`
- `src/Nova.Playback/BoundedPlaybackScheduler.cs`
- `src/Nova.Playback/PlaybackLateAwareContinuousPumpBoundary.cs`
- `src/Nova.Playback/PlaybackRecoveryAwarePumpOrchestrator.cs`
- `src/Nova.Playback/PlaybackDeadlineAwareRunBudgetPolicyBoundary.cs`

## 最新のADR

- `docs/adr/ADR-0094-diagnostic-packet-frame-transaction.md`
- `docs/adr/ADR-0095-diagnostic-frame-metadata-transaction.md`
- `docs/adr/0096-bounded-diagnostic-frame-data-transaction.md`
- `docs/adr/ADR-0097-diagnostic-managed-frame-copy-transaction.md`
- `docs/adr/ADR-0098-diagnostic-managed-format-conversion-transaction.md`
- `docs/adr/ADR-0099-diagnostic-awaited-sink-delivery-transaction.md`
- `docs/adr/ADR-0100-playback-cutover-architecture-review.md`

## 最新の検証文書

- `docs/reviews/PCN-0102-DIAGNOSTIC-PACKET-FRAME-TRANSACTION-VALIDATION.md`
- `docs/reviews/PCN-0103-DIAGNOSTIC-FRAME-METADATA-TRANSACTION-VALIDATION.md`
- `docs/reviews/PCN-0104-DIAGNOSTIC-FRAME-DATA-TRANSACTION-VALIDATION.md`
- `docs/reviews/PCN-0105-DIAGNOSTIC-MANAGED-FRAME-COPY-TRANSACTION-VALIDATION.md`
- `docs/reviews/PCN-0106-DIAGNOSTIC-MANAGED-FORMAT-CONVERSION-TRANSACTION-VALIDATION.md`
- `docs/reviews/PCN-0107-DIAGNOSTIC-AWAITED-SINK-DELIVERY-TRANSACTION-VALIDATION.md`
- `docs/reviews/PCN-0108-PLAYBACK-CUTOVER-ARCHITECTURE-REVIEW.md`

## 検証入口

- 全体: `build.ps1`
- build: `dotnet build PeerCast-Nova.sln --configuration Release --no-restore`
- test: `dotnet test PeerCast-Nova.sln --configuration Release --no-build`
- format: `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`

## 次の作業

**0.0.8q — Bounded Scheduler Aggregate Byte Budget**

`BoundedPlaybackScheduler`へ既存frame-count capacityと独立したaggregate owned-byte budgetを追加する。checked byte accounting、single-frame oversize拒否、count/byte両制約に対するfull-policy動作、reservation保護、snapshot、drop/dispose時の減算、overflow fail-closedをfocused testsで固定する。

0.0.8qがVerifiedになるまで、診断decoderからschedulerへのmanaged-frame ownership transferは接続しない。

## まだ完了を主張しないもの

- FFmpeg backend activationまたは配布承認
- FFmpeg同梱判断の法務サインオフ
- bounded FLV/MP4 demux contract
- diagnosticまたはproduction decoderからschedulerへのframe ingress
- decoded bufferingのduration cutoff
- precise deadline scheduling、in-flight preemption、drift correction、完全なA/V同期
- audible/visible real-media playback
- approved runtimeとbounded demux inputによる全boundaryのstart-recover-stop実行
- GUI、installer、recorder
