# PeerCast Nova Navigation

## 現在地

**0.0.9v — FFmpeg Candidate License, Backend Registration, and Distribution Review — Completed**

pinned BtbN `win64-lgpl-shared` candidateのlicense class、既定配布方式、利用者replacement、production registration locationを確定しました。

- candidate classはLGPLv3 shared-library configuration
- GPL／nonfree variantsは拒否
- Novaの既定releaseへFFmpeg binaryを含めない
- mirror／rehost／download／extract／install／updateなし
- user／host supplied explicit runtime directoryだけを使用
- `PATH`／registry／implicit directory discoveryなし
- exact filename／SHA-256／ABI major／required symbol検証を維持
- built-in fixed hashesだけを唯一のactivated replacement routeにはしない
- host-owned manifestでinterface-compatible modified LGPL buildを利用者が明示承認
- production registrationはgeneric backend factoryではなくapplication composition root
- binary bundlingは別ADRとcomplete source／notice archiveなしでは禁止
- exact candidateは条件付き承認可能だが、compliance implementation前なので`LicenseReview.Pending`を維持
- runtime source／tests変更なし

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終branch headでrestore、Release build、full tests、formatを通してからmergeします。

## 最新の入口

- `docs/adr/ADR-0132-ffmpeg-candidate-license-backend-registration-distribution-review.md`
- `docs/reviews/PCN-0140-FFMPEG-CANDIDATE-LICENSE-BACKEND-REGISTRATION-DISTRIBUTION-REVIEW.md`
- `docs/legal/FFMPEG-EXTERNAL-RUNTIME-COMPLIANCE.md`
- `docs/legal/THIRD-PARTY-AND-PROVENANCE.md`
- `src/Nova.Playback/PlaybackFfmpegBackendCandidate.cs`
- `src/Nova.Playback/PlaybackDecoderBackendPolicy.cs`
- `src/Nova.Playback/PlaybackProductionDecoderGenerationFactory.cs`
- `src/Nova.Playback/PlaybackProductionPcpFlvCompositionBoundary.cs`

## ADR／PCN索引の永久欠番

- ADR-0082: 採番ミスによる永久欠番。番号を遡って再利用しない。
- ADR-0096: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。
- PCN-0104: ADR-0082と同様、0.0.8lの文書追加が未完了のまま後続採番へ進んだ採番ミスによる永久欠番。番号を遡って再利用しない。

## 次の作業

**0.0.9w — External LGPL Runtime Compliance and User Replacement Boundary**

bounded built-in／host manifestを実装し、LGPLv3 license class、exact DLL role／filename／SHA-256／ABI major／symbol setを検証します。GPLv3／LGPLv3本文、third-party notice、replacement手順を配布物へ追加し、利用者がmodified compatible buildを明示manifestで承認できることをtestします。全条件通過後だけcandidateを`Approved`へ変更します。

## 未完了

- external LGPL runtime compliance／host manifest implementation
- license text／third-party notice packaging
- candidate approval transition
- approved production decoder ingress activation
- Windows application／window／device lifecycle
- controlled real-media decode／output validation
- MP4／fragmented MP4 parser
- discontinuity-time `avcodec_flush_buffers` semantics
- duration cutoff
- complete A/V synchronization
- controlled audible／visible playback
- GUI、installer、recorder
