# PeerCast Nova Navigation

## 現在地

**Milestone 0.0.8f — Diagnostic Fixture Adapter**

- 候補記録と技術検証: 完了
- FFmpeg backend activation: 未承認
- 実decoder・可視映像・可聴音声: 未完成
- current: 0.0.8e fixtureを0.0.8d RuntimeDecode phaseへ渡す診断専用adapterを実装済み。native context以降と実メディア遷移は未実装
- safety: `PlaybackDecoderBackendFactory`等から新規再生境界へのactivation参照はなく、未承認状態を維持
- distribution: ユーザー/ホスト導入、動的リンク、NovaによるFFmpeg同梱・再配布なし
- trust boundary: verified 5-DLL handles remain open until lease disposal
- path boundary: local fixed drives only; UNC and reparse points are rejected
- enum boundary: undefined provenance, container, codec, and result values fail closed

## 進捗記録の規律

- ADR-0082は採番ミスによる永久欠番。履歴・現行ツリー・関連PRに予約または破棄の記録はなく、ADR-0081の次が最初からADR-0083として作成された。
- PCN検証文書は既存規則に従い、今後も `docs/reviews/` に配置する。
- milestone実装PRには、同じPR内で `CHANGELOG.md` と本ファイルの現在地・索引更新を含める。
- activation、実機再生、法務承認など未完了事項は、実装済み境界と混同せず明示する。
- 診断統合ハーネスは本番経路から独立し、decoder backendのactivation状態を変更しない。
- 本棚卸しで `CHANGELOG.md` と本ファイルを0.0.8bまで照合済み。

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
| 0.0.8e | Bounded Diagnostic Playback Fixture | Implemented; adapter not connected |
| 0.0.8e | Bounded Diagnostic Playback Fixture | Implemented; adapter not connected |

## 最新のコード入口

- `src/Nova.Playback/PlaybackLateItemDispositionPolicyBoundary.cs`
- `src/Nova.Playback/PlaybackLateDispatchCoordinationBoundary.cs`
- `src/Nova.Playback/PlaybackLateReservationDispositionBoundary.cs`
- `src/Nova.Playback/PlaybackLateAwareSchedulerDecisionBoundary.cs`
- `src/Nova.Playback/PlaybackLateAwareTimedDispatchBoundary.cs`
- `src/Nova.Playback/PlaybackLateAwareContinuousPumpBoundary.cs`
- `src/Nova.Playback/PlaybackAudioRecoveryBoundary.cs`
- `src/Nova.Playback/PlaybackRecoveryAwarePumpOrchestrator.cs`
- `src/Nova.Playback/PlaybackDeadlineAwareRunBudgetPolicyBoundary.cs`
- `src/Nova.Playback/PlaybackPipelineIntegrationProbe.cs`
- `src/Nova.Playback/PlaybackDiagnosticFixtureAdapter.cs`
- `src/Nova.Cli/PlaybackPipelineIntegrationProbeCommand.cs`
- `src/Nova.Playback/BoundedDiagnosticPlaybackFixture.cs`
- `src/Nova.Playback/BoundedDiagnosticPlaybackFixture.cs`

## 最新のADR

- `docs/adr/ADR-0077-TIMED-SCHEDULER-DISPATCH-BOUNDARY.md`
- `docs/adr/ADR-0078-LATE-ITEM-DISPOSITION-POLICY.md`
- `docs/adr/ADR-0079-LATE-DISPATCH-COORDINATION-BOUNDARY.md`
- `docs/adr/ADR-0080-LATE-RESERVATION-DISPOSITION-BOUNDARY.md`
- `docs/adr/ADR-0081-LATE-AWARE-SCHEDULER-DECISION-BOUNDARY.md`
- ADR-0082: permanent gap caused by numbering error; no file is to be created retroactively
- `docs/adr/ADR-0083-LATE-AWARE-TIMED-DISPATCH-BOUNDARY.md`
- `docs/adr/ADR-0084-LATE-AWARE-CONTINUOUS-PUMP-BOUNDARY.md`
- `docs/adr/ADR-0085-audio-recovery-boundary.md`
- `docs/adr/ADR-0086-recovery-aware-pump-orchestrator.md`
- `docs/adr/ADR-0087-deadline-aware-run-budget-policy.md`
- `docs/adr/ADR-0088-diagnostic-playback-pipeline-integration-probe.md`
- `docs/adr/ADR-0089-bounded-diagnostic-playback-fixture.md`
- `docs/adr/ADR-0090-diagnostic-fixture-adapter.md`
- `docs/adr/ADR-0089-bounded-diagnostic-playback-fixture.md`

## 最新の検証文書

- `docs/reviews/PCN-0086-TIMED-SCHEDULER-DISPATCH-VALIDATION.md`
- `docs/reviews/PCN-0087-LATE-ITEM-DISPOSITION-POLICY-VALIDATION.md`
- `docs/reviews/PCN-0088-LATE-DISPATCH-COORDINATION-VALIDATION.md`
- `docs/reviews/PCN-0089-LATE-RESERVATION-DISPOSITION-VALIDATION.md`
- `docs/reviews/PCN-0090-LATE-AWARE-SCHEDULER-DECISION-VALIDATION.md`
- `docs/reviews/PCN-0091-LATE-AWARE-TIMED-DISPATCH-VALIDATION.md`
- `docs/reviews/PCN-0092-LATE-AWARE-CONTINUOUS-PUMP-VALIDATION.md`
- `docs/reviews/PCN-0093-AUDIO-RECOVERY-BOUNDARY-VALIDATION.md`
- `docs/reviews/PCN-0094-RECOVERY-AWARE-PUMP-ORCHESTRATOR-VALIDATION.md`
- `docs/reviews/PCN-0095-DEADLINE-AWARE-RUN-BUDGET-POLICY-VALIDATION.md`
- `docs/reviews/PCN-0096-DIAGNOSTIC-PLAYBACK-PIPELINE-INTEGRATION-PROBE-VALIDATION.md`
- `docs/reviews/PCN-0097-BOUNDED-DIAGNOSTIC-PLAYBACK-FIXTURE-VALIDATION.md`
- `docs/reviews/PCN-0098-DIAGNOSTIC-FIXTURE-ADAPTER-VALIDATION.md`
- `docs/reviews/PCN-0097-BOUNDED-DIAGNOSTIC-PLAYBACK-FIXTURE-VALIDATION.md`

## 検証入口

- 全体: `build.ps1`
- build: `dotnet build PeerCast-Nova.sln --configuration Release --no-restore`
- test: `dotnet test PeerCast-Nova.sln --configuration Release --no-build`
- format: `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`

## 次の作業

0.0.8e fixtureを0.0.8dの`RuntimeDecode` phaseへ渡す診断専用adapterを、license gateより後かつnative ownershipより前に設計する。Pending review中はadapterを実行しない。

## まだ完了を主張しないもの

- FFmpeg backend activationまたは配布承認
- FFmpeg同梱判断の法務サインオフ
- 実codecによるFLV demux/decode
- decoded frame queue、renderer、audio deviceの実機統合
- precise deadline scheduling、in-flight preemption、drift correction、完全なA/V同期
- audible/visible real-media playback
- approved runtimeとbounded demux inputによる全boundaryのstart-recover-stop実行
- GUI、installer、recorder
