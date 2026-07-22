# PeerCast Nova Navigation

## 現在地

**0.0.10d — Controlled Physical Windows Video／Audio Output Validation — Phase 4 Composition In Progress**

application-owned runner、approved external-runtime lifecycle、digest-first controlled FLV fixture replay、finite decode／delivery、pre-cleanup native evidence、production-first cleanup、borrowed owner verification、Phase 1 evidence gateを一つの明示compositionへ接続しました。通常CLIから隔離した専用`Nova.ControlledWindowsRunner` entrypointを追加しています。実機D3D11／WASAPI runはまだ実行していません。

- runtime directory、fixture path、SHA-256、video／audio kind、operation上限をrunner起動前にfail-closed検証
- fixture全体を上限内で読み、完全なSHA-256一致後にだけPCP packet viewを公開
- digest mismatchではpacket consumerを一度も呼ばない
- callback-scoped payloadは有限chunkへ分割し、replay終了後にtemporary bufferを消去
- runner → lifecycle → fixture → input completion → decode／drain → deliveryの順序を固定
- decode／delivery回数はimmutable上限内、sleep／timer／polling／hidden retryなし
- caller-visible `RetainedWouldBlock`は1回記録してrunを終了し、再試行しない
- native output snapshotはlifecycle cleanup前に取得
- cleanup完了後に元のwindow token、message loop、dispatcher owner-thread accessを再検証
- exact runtime build ID、fixture digest、generation、delivery counter、cleanup contractをPhase 1 gateへ提出
- evidence admissionまたは失敗後にapplication runnerを停止
- command出力はsanitized counter／resultのみでlocal path、device ID、pointer、handle、media bytesを出力しない
- dedicated entrypointのみがcommandを呼び、通常`Nova.Cli` startupは変更しない
- Linux CIはfake operationsとtemporary fixtureだけを使い、physical outputを主張しない

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終headでrestore、Release build with analyzers、full solution tests、format verificationを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0142-explicit-controlled-windows-physical-run-composition.md`
- `docs/reviews/PCN-0150-EXPLICIT-CONTROLLED-WINDOWS-PHYSICAL-RUN-COMPOSITION.md`
- `src/Nova.ControlledWindowsRunner/Program.cs`
- `src/Nova.ControlledWindowsRunner/Nova.ControlledWindowsRunner.csproj`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunComposition.cs`
- `src/Nova.Cli/SystemPlaybackControlledWindowsPhysicalRunOperations.cs`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunCommand.cs`
- `src/Nova.Network/Pcp/PcpControlledFlvFixtureReplayBoundary.cs`
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsPhysicalRunCompositionTests.cs`
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsPhysicalRunCommandTests.cs`
- `tests/Nova.Network.Tests/PcpControlledFlvFixtureReplayBoundaryTests.cs`
- `docs/adr/ADR-0141-application-owned-windows-physical-runner-harness.md`
- `docs/reviews/PCN-0149-APPLICATION-OWNED-WINDOWS-PHYSICAL-RUNNER-HARNESS.md`
- `src/Nova.Cli/PlaybackWindowsPhysicalRunnerHost.cs`
- `src/Nova.Cli/SystemPlaybackWindowsPhysicalRunnerNative.cs`
- `docs/adr/ADR-0140-system-native-windows-output-evidence-capture.md`
- `docs/reviews/PCN-0148-SYSTEM-NATIVE-WINDOWS-OUTPUT-EVIDENCE-CAPTURE.md`
- `src/Nova.Playback/PlaybackWindowsOutputNativeEvidenceCapture.cs`
- `docs/adr/ADR-0139-controlled-physical-windows-output-evidence-admission-boundary.md`
- `docs/reviews/PCN-0147-CONTROLLED-PHYSICAL-WINDOWS-OUTPUT-EVIDENCE-ADMISSION.md`
- `src/Nova.Playback/PlaybackControlledWindowsOutputEvidenceBoundary.cs`
- `docs/adr/ADR-0138-controlled-windows-video-audio-output-validation-review.md`
- `docs/reviews/PCN-0146-CONTROLLED-WINDOWS-VIDEO-AUDIO-OUTPUT-VALIDATION-REVIEW.md`
- `src/Nova.Playback/PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary.cs`

## 進捗記録の規律

実装・設計変更だけでなく、バグ修正にも同じ進捗記録の規律を適用します。

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.10d Phase 5 — Independent Physical Video and Audio Runs**

明示Windows 11 physical runnerでexact approved runtimeとretained controlled fixtureを使用し、videoとaudioを独立実行します。accepted `SystemWindows` evidence、sanitized command output、operator observationを記録し、両runが通るまで0.0.10d完了を主張しません。

## 未完了

- controlled physical Windows video output validation
- controlled physical Windows audio output validation
- combined controlled audible／visible playback validation
- GUI legal／about surface
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- GUI、installer、recorder
