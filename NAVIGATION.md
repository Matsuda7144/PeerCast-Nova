# PeerCast Nova Navigation

## 現在地

**0.0.10a — Approved FFmpeg Candidate Manifest Refresh — Implemented**

approved BtbN 2026-07-21 win64 LGPL shared候補をbuilt-in manifestへ実装し、complete inventory、5 load-role digest、build configuration、Windows dependency、superseded candidate履歴をfail-closed testsで固定しました。native activationは行っていません。

- active build IDとfixed release／archive／commit identity
- exact ten-file `bin` inventoryと5 load-role ABI／symbol条件
- GPL／nonfree disabled、shared enabled、static disabled
- four Windows system dependenciesとunknown dependency fail-close
- complete transcripts／inventory text evidence
- superseded 2026-07-15 rejection history
- FFmpeg binary非配布とnative activation未実施

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終headでrestore、Release build with analyzers、full solution tests、format verificationを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0137-approved-ffmpeg-candidate-manifest-refresh.md`
- `docs/reviews/PCN-0145-APPROVED-FFMPEG-CANDIDATE-MANIFEST-REFRESH.md`
- `src/Nova.Playback/PlaybackFfmpegBackendCandidate.cs`
- `src/Nova.Playback/PlaybackFfmpegExternalRuntimeManifest.cs`
- `src/Nova.Playback/PlaybackDecoderBackendRuntimeInventory.cs`
- `THIRD-PARTY-LICENSES/FFmpeg/PROVENANCE.md`
- `tests/Nova.Playback.Tests/PlaybackControlledRetainedFixtureProductionActivationValidationTests.cs`
- `docs/adr/ADR-0136-controlled-retained-fixture-production-activation-validation.md`
- `docs/reviews/PCN-0144-CONTROLLED-RETAINED-FIXTURE-PRODUCTION-ACTIVATION-VALIDATION.md`
- `docs/adr/ADR-0135-finite-approved-external-runtime-windows-lifecycle-activation-boundary.md`
- `docs/reviews/PCN-0143-FINITE-APPROVED-EXTERNAL-RUNTIME-WINDOWS-LIFECYCLE-ACTIVATION-BOUNDARY.md`
- `src/Nova.Playback/PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary.cs`
- `src/Nova.Playback/PlaybackApprovedExternalRuntimeWindowsLifecycleOperations.cs`
- `src/Nova.Playback/PlaybackDispatcherBackedProductionGeneration.cs`
- `src/Nova.Playback/PlaybackWindowsOutputDispatcher.cs`
- `src/Nova.Playback/PlaybackProductionPcpFlvCompositionBoundary.cs`
- `src/Nova.Playback/PlaybackGenerationMatchedSchedulerDeliveryBoundary.cs`
- `tests/Nova.Playback.Tests/PlaybackControlledRetainedFlvFixtureValidationTests.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.10b — Controlled Real External-Runtime Native Decode Validation**

exact approved built-in runtimeを実際に配置したcontrolled Windows環境で、H.264／AAC native context、packet／frame、metadata、bounds、cleanupを有限検証します。physical rendering／audio outputは後続gateとして分離します。

## 未完了

- controlled real FFmpeg native decode validation
- controlled Windows video／audio output validation
- Windows application window／message loop
- application dispatcher implementation
- GUI legal／about surface
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- controlled audible／visible playback
- GUI、installer、recorder
