# PeerCast Nova Navigation

## 現在地

**Milestone 0.0.6c2-fix1 — Verified FFmpeg Runtime Lease**

- 候補記録と技術検証: 完了
- FFmpeg backend activation: 未承認
- 実decoder・映像表示・音声出力: 未実装
- runtime 5 DLLの名前・サイズ・SHA-256固定と事前照合: 完了
- 配布方針: ユーザー/ホスト導入、動的リンク、Novaによる同梱・再配布なし
- locator: 明示指定された1ディレクトリだけを検査
- 次: 0.0.6d FFmpeg ABI / Symbol Gate
- trust boundary: verified 5-DLL handles remain open until lease disposal
- path boundary: local fixed drives only; UNC and reparse points are rejected
- enum boundary: undefined provenance and container values fail closed

## 検証済みマイルストーン

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
| 0.0.6c2-fix1 | Runtime Lease CI and Artifact Consistency | Windows verified; Ubuntu CI execution pending |

## 0.0.6b固定候補

- 配布元: BtbN/FFmpeg-Builds
- Asset: `ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip`
- FFmpeg: `n8.1.2-22-g94138f6973-20260715`
- Archive SHA-256:
  `F711E0478C03FD48624C59A0487E0C5D904779A3C67280A0A7B49B0002F12B43`
- shared有効、static無効
- GPL/nonfree configure flagなし
- `flv` / `live_flv` demuxerあり
- LGPLv3 license本文あり
- Nova成果物へのFFmpeg binary同梱なし
- provenance reviewは`Pending`のためactivation不可

## コード入口

### Playback input・分類

- `src/Nova.Playback/PcpPlaybackLatencyBuffer.cs`
- `src/Nova.Playback/PlaybackMediaClassifier.cs`
- `src/Nova.Playback/PlaybackMediaClassificationModels.cs`

### Decoder lifecycle・decoded output

- `src/Nova.Playback/PlaybackDecoderRunner.cs`
- `src/Nova.Playback/IPlaybackDecoder.cs`
- `src/Nova.Playback/IPlaybackDecodedFrameSink.cs`
- `src/Nova.Playback/PlaybackDecodedFrameModels.cs`

### Backend・provenance・artifact

- `src/Nova.Playback/IPlaybackDecoderBackend.cs`
- `src/Nova.Playback/PlaybackDecoderBackendFactory.cs`
- `src/Nova.Playback/PlaybackDecoderBackendModels.cs`
- `src/Nova.Playback/PlaybackDecoderBackendPolicy.cs`
- `src/Nova.Playback/PlaybackDecoderBackendArtifactVerifier.cs`
- `src/Nova.Playback/PlaybackFfmpegBackendCandidate.cs`
- `src/Nova.Playback/PlaybackDecoderBackendRuntimeInventory.cs`
- `src/Nova.Playback/PlaybackDecoderBackendRuntimeInventoryVerifier.cs`
- `src/Nova.Playback/PlaybackDecoderBackendInstalledRuntimeLocator.cs`

## 検証入口

- 全体: `build.ps1`
- format: `dotnet format PeerCast-Nova.sln --verify-no-changes`
- lifecycle反復: `tools/Test-DecoderLifecycle.ps1`
- backend反復: `tools/Test-DecoderBackendBoundary.ps1`
- runtime lease反復: `tools/Test-VerifiedRuntimeLease.ps1`
- FFmpeg候補: `tools/Test-FfmpegBackendCandidate.ps1`
- 実機lifecycle: `tools/Test-PeerCastStationDecoderLifecycle.ps1`

## 設計・検証文書

- `docs/adr/ADR-0039-REPLACEABLE-DECODER-LIFECYCLE.md`
- `docs/adr/ADR-0041-DECODED-OUTPUT-CONTRACT.md`
- `docs/adr/ADR-0042-DECODER-BACKEND-BOUNDARY.md`
- `docs/adr/ADR-0045-DECODER-BACKEND-PROVENANCE-POLICY.md`
- `docs/adr/ADR-0047-DECODER-BACKEND-ARTIFACT-MANIFEST.md`
- `docs/adr/ADR-0048-EXTERNAL-BACKEND-ARTIFACT-VERIFICATION.md`
- `docs/adr/ADR-0049-PINNED-FFMPEG-BACKEND-CANDIDATE.md`
- `docs/adr/ADR-0051-USER-PROVIDED-FFMPEG-RUNTIME.md`
- `docs/adr/ADR-0052-VERIFIED-FFMPEG-RUNTIME-LEASE.md`
- `docs/reviews/PCN-0058-PINNED-FFMPEG-CANDIDATE-VALIDATION.md`
- `docs/reviews/PCN-0060-EXPLICIT-INSTALLED-RUNTIME-LOCATOR-VALIDATION.md`
- `docs/reviews/PCN-0061-VERIFIED-FFMPEG-RUNTIME-LEASE-VALIDATION.md`
- `docs/legal/THIRD-PARTY-AND-PROVENANCE.md`

## 次の作業

0.0.6dでは、0.0.6c2のverified runtime leaseを直接受け取り、leaseが保持する
同一DLLだけを読み込みます。元pathからの再探索・再検証・再openは禁止します。
期待するABI majorと
必須symbolだけを検査して直ちに解放するgateを設計します。media inputやdecoder
instanceはまだ作らず、license reviewも`Pending`のまま維持します。

## まだ完了を主張しないもの

- FFmpeg backend activationまたは配布承認
- FFmpeg同梱判断の法務サインオフ（表示義務の履行含む）
- 実codecによるFLV demux/decode
- decoded frame queue、renderer、audio device
- A/V clockと同期
- audible/visible real-media playback
- GUI、installer、recorder
