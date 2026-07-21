# PeerCast Nova Navigation

## 現在地

**0.0.9w — External LGPL Runtime Compliance and User Replacement Boundary — Completed**

0.0.9vで選択したexternal LGPL runtime方針をruntime、tests、配布legal materialへ実装しました。

- immutable built-in／host-supplied manifest
- accepted licenseは`LGPL-3.0-or-later`だけ
- exact five DLL roles／filenames／ABI majors／lengths／SHA-256
- GPL／nonfree／unknown／LGPL-2.1／duplicate／missing／renamed／path-bearing／wrong-ABI manifestを拒否
- user-selected modified LGPL buildをhost manifestで明示承認
- replacementでもchecksum、PE dependency、ABI、required-symbol検証を維持
- one explicit runtime directoryのみ使用
- `PATH`／registry／implicit directory／network／download／fallbackなし
- production entryは`PlaybackFfmpegExternalRuntimeComposition`
- generic `PlaybackDecoderBackendFactory`へのproduction登録なし
- pinned candidateは`LicenseReview.Approved`
- GPLv3／LGPLv3本文、FFmpeg notice、replacement手順、text hashを収録
- CLI build／publish outputへlegal directoryをコピー
- FFmpeg binary bundling／mirror／rehost／download／extract／install／updateは禁止継続
- Windows lifecycle／device ownershipとcontrolled real-media playbackは未完了

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。implementation headでrestore、Release build with analyzers、full solution tests、format verificationが成功しました。最終documentation headも同じgateを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0133-external-lgpl-runtime-compliance-user-replacement-boundary.md`
- `docs/reviews/PCN-0141-EXTERNAL-LGPL-RUNTIME-COMPLIANCE-USER-REPLACEMENT-BOUNDARY.md`
- `docs/legal/FFMPEG-EXTERNAL-RUNTIME-COMPLIANCE.md`
- `docs/legal/THIRD-PARTY-AND-PROVENANCE.md`
- `THIRD-PARTY-LICENSES/FFmpeg/NOTICE.md`
- `THIRD-PARTY-LICENSES/FFmpeg/RUNTIME-REPLACEMENT.md`
- `src/Nova.Playback/PlaybackFfmpegExternalRuntimeManifest.cs`
- `src/Nova.Playback/PlaybackFfmpegExternalRuntimeProductionComposition.cs`
- `src/Nova.Playback/PlaybackFfmpegBackendCandidate.cs`
- `src/Nova.Playback/PlaybackProductionPcpFlvCompositionBoundary.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9x — Approved External Runtime and Windows Lifecycle Activation Review**

承認済みmanifest、explicit runtime directory、application-owned Windows adapter／COM／window／device lifecycle、borrowed output boundary、production compositionの正確な構築・停止・reset・replacement順序をレビューします。controlled retained-fixture／real-media Windows validationの入口を定義し、resource二重所有、implicit fallback、隠れたresident pumpを実装前に排除します。

## 未完了

- approved external runtimeとWindows application lifecycleの接続
- Windows window／adapter／COM／device lifecycle composition
- controlled production retained-fixture validation
- controlled real-media decode／output validation
- GUI legal／about surface
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- controlled audible／visible playback
- GUI、installer、recorder