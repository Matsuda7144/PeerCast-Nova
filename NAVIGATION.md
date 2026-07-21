# PeerCast Nova Navigation

## 現在地

**0.0.9u — Finite Production Generation Ingress and Replacement Composition Boundary — Implemented**

0.0.9tで確定したproduction ingress／transfer／delivery／replacement契約をruntimeへ実装しました。既存diagnostic compositionは変更せず、production専用ownerを追加しています。

- one parser、one production creator、最大one complete generation
- application-owned Windows output boundaryは非所有borrow
- exact decode snapshot／submission／one-frame receiveをinternal forwarding
- raw decode／native／ABI／runtime ownershipは非公開
- packet、finalization、receive／drain、transfer、delivery、reset、replacement、stopをone gateで直列化
- decoded completionをtransfer前にexact identity／sequence／managed bytesで検証
- `Transferred`後のcompletion access／dispose禁止
- caller-driven one-item deliveryはPCP callback外
- retained delivery resultはscheduler reservationを保持
- scheduler admission rejectionはterminal、capacity polling／hidden retryなし
- old generation `StopAsync`完了後だけparser discontinuity reset
- replacement作成はnew exact source description後だけ
- overlap、old revival、rollback、partial packet commitなし
- generation→parser／source→factoryのordered async cleanup
- ten focused cross-platform tests
- pinned candidateは`LicenseReview.Pending`のまま
- normal backend registration、device activation、distributionは未変更

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0131-finite-production-generation-ingress-replacement-composition-boundary.md`
- `docs/reviews/PCN-0139-FINITE-PRODUCTION-GENERATION-INGRESS-REPLACEMENT-COMPOSITION-BOUNDARY.md`
- `src/Nova.Playback/PlaybackProductionPcpFlvCompositionBoundary.cs`
- `src/Nova.Playback/PlaybackProductionDecoderGenerationIngressForwarding.cs`
- `tests/Nova.Playback.Tests/PlaybackProductionPcpFlvCompositionBoundaryTests.cs`
- `src/Nova.Playback/PlaybackProductionDecoderGenerationFactory.cs`
- `src/Nova.Playback/PlaybackOneEventFinalEosFlvParser.cs`
- `src/Nova.Playback/PlaybackSourceBoundOneFrameDecodeBoundary.cs`
- `src/Nova.Playback/PlaybackDecodedCompletionSchedulerIngressBoundary.cs`
- `src/Nova.Playback/PlaybackGenerationMatchedSchedulerDeliveryBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputBufferProcessingBoundary.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9v — FFmpeg Candidate License, Backend Registration, and Distribution Review**

pinned candidateの正確なprovenanceとlicense条件を確認し、host-installed dynamic loading、normal backend registration、source／notice義務、bundling／redistribution可否を明示的に判断します。未承認経路は引き続きfail closedです。

## 未完了

- FFmpeg candidate license approval and backend registration
- runtime bundling／distribution decision
- approved production decoder ingress activation
- Windows application／window／device lifecycle
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- controlled audible／visible playback
- GUI、installer、recorder
