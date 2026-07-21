# PeerCast Nova Navigation

## 現在地

**0.0.9x — Approved External Runtime and Windows Lifecycle Activation Review — Completed**

exact built-in approved external FFmpeg production routeと、explicit host-attested unreviewed replacement route、既存Windows output境界をapplication-owned lifecycleへ接続するownershipと順序を確定しました。

- applicationがwindow、message loop、dispatcher／output owner threadを所有
- playback lifecycleがprobe、COM／D3D11／WASAPI activation、binding、stream、buffer、production compositionを所有
- windowはopaque tokenとして借用し、playback側では作成／破棄しない
- activation resourceへの全アクセスとteardownをowner-thread dispatcher経由に限定
- playback-owned thread／queue／message pump／timer／resident loopなし
- 0.0.9q generation-matched deliveryを唯一のproduction scheduler-output ownerとして維持
- 旧0.0.6y scheduler adapterをproduction経路へ追加しない
- exact stream description後にactive decoder generationでoutputを初期化
- discontinuity replacementは同じdevice上でoutput generationをreconfigure
- resizeは明示操作、retained itemを消費しない
- device lossは初回lifecycle implementationではterminal
- stopはproduction composition後にbuffer → stream → binding → activation
- FFmpeg external-only／explicit-directory／verified／unbundled policyを維持
- host manifestは`HostDeclared`／`HostAttested`／`Unreviewed`でありNova approvalではない
- 0.0.9x自体のruntime source／tests変更なし

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終headでrestore、Release build with analyzers、full solution tests、format verificationを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0134-approved-external-runtime-windows-lifecycle-activation-review.md`
- `docs/reviews/PCN-0142-APPROVED-EXTERNAL-RUNTIME-WINDOWS-LIFECYCLE-ACTIVATION-REVIEW.md`
- `docs/adr/ADR-0133-external-lgpl-runtime-compliance-user-replacement-boundary.md`
- `docs/reviews/PCN-0141-EXTERNAL-LGPL-RUNTIME-COMPLIANCE-USER-REPLACEMENT-BOUNDARY.md`
- `docs/legal/FFMPEG-EXTERNAL-RUNTIME-COMPLIANCE.md`
- `THIRD-PARTY-LICENSES/FFmpeg/PROVENANCE.md`
- `THIRD-PARTY-LICENSES/FFmpeg/NOVA-DISTRIBUTION-GUARD.md`
- `src/Nova.Playback/PlaybackFfmpegExternalRuntimeProductionComposition.cs`
- `src/Nova.Playback/PlaybackGenerationMatchedSchedulerDeliveryBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputAdapterSelection.cs`
- `src/Nova.Playback/PlaybackWindowsOutputActivationBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputResourceBindingBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputStreamInitializationBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputBufferProcessingBoundary.cs`
- `docs/adr/ADR-0069-WINDOWS-OUTPUT-ADAPTER-SELECTION.md`
- `docs/adr/ADR-0070-WINDOWS-OUTPUT-ACTIVATION-BOUNDARY.md`
- `docs/adr/ADR-0071-WINDOWS-OUTPUT-RESOURCE-BINDING-BOUNDARY.md`
- `docs/adr/ADR-0072-WINDOWS-OUTPUT-STREAM-INITIALIZATION-BOUNDARY.md`
- `docs/adr/ADR-0073-WINDOWS-OUTPUT-BUFFER-PROCESSING-BOUNDARY.md`
- `docs/adr/ADR-0074-WINDOWS-OUTPUT-SCHEDULER-ADAPTER-BOUNDARY.md`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9y — Finite Approved External Runtime and Windows Lifecycle Activation Boundary**

application-owned dispatcherとwindow tokenを借用する一つの有限lifecycle ownerを実装します。exact built-in approvedまたはexplicit host-attested runtimeを対象に、initial stream configuration、discontinuity output generation reconfiguration、retained resize／backpressure、terminal device loss、production-first stop、owner-thread teardownをcross-platform injected testsで固定します。

## 未完了

- approved／host-attested runtime Windows lifecycle implementation
- output owner-thread dispatcher adapter
- controlled production retained-fixture validation
- controlled real-media decode／output validation
- Windows application window／message loop
- GUI legal／about surface
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- controlled audible／visible playback
- GUI、installer、recorder
