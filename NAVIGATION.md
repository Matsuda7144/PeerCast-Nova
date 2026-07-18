# PeerCast Nova Navigation

## 現在地

**Milestone 0.0.7a — A/V Timing Decision Boundary**

- 候補記録と技術検証: 完了
- FFmpeg backend activation: 未承認
- 実decoder・映像表示・音声出力: 未実装
- runtime 5 DLLの名前・サイズ・SHA-256固定と事前照合: 完了
- 配布方針: ユーザー/ホスト導入、動的リンク、Novaによる同梱・再配布なし
- locator: 明示指定された1ディレクトリだけを検査
- current: one-item A/V timing actions are decided from presentation timestamps and monotonic playback position
- trust boundary: verified 5-DLL handles remain open until lease disposal
- path boundary: local fixed drives only; UNC and reparse points are rejected
- enum boundary: undefined provenance, container, codec, and result values fail closed

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
| 0.0.6d | FFmpeg ABI / Symbol Gate | Implemented; activation pending |
| 0.0.6e | Native Decoder Context Lease | Implemented; codec open pending |
| 0.0.6f | Codec Parameter Ownership Boundary | Implemented; native transfer pending |
| 0.0.6g | Native Parameter Transfer Boundary | Implemented; codec open pending |
| 0.0.6h | avcodec_open2 Boundary | Implemented; packet/frame processing pending |
| 0.0.6i | Packet / Frame Processing Boundary | Implemented; frame extraction pending |
| 0.0.6j | Callback-Scoped Native Frame Boundary | Implemented; metadata extraction pending |
| 0.0.6k | Native Frame Metadata Extraction | Implemented; plane copy/conversion pending |
| 0.0.6l | Frame Plane / Sample Boundary | Implemented; managed copy/conversion pending |
| 0.0.6m | Managed Frame Copy | Implemented; conversion/output pending |
| 0.0.6n | Decoded Output Adapter | Implemented; YUV/planar conversion pending |
| 0.0.6o | Managed Format Conversion | Implemented; delivery pending |
| 0.0.6p | Awaited Decoded Sink Delivery | Implemented; playback policy pending |
| 0.0.6q | Playback Policy / Clock Boundary | Implemented; scheduling pending |
| 0.0.6r | Bounded Playback Scheduler | Implemented; renderer/device pending |
| 0.0.6s | Renderer / Audio Sink Boundary | Implemented; OS adapters pending |
| 0.0.6t | Windows Output Adapter Selection | Implemented; OS activation pending |
| 0.0.6u | Windows Output Activation Boundary | Implemented; rendering and stream initialization pending |
| 0.0.6v | Windows Output Resource Binding Boundary | Implemented; rendering and stream initialization pending |
| 0.0.6w | Windows Output Stream Initialization Boundary | Implemented; rendering, Present, and audio Start pending |
| 0.0.6x | Windows Output Buffer Processing Boundary | Implemented; scheduling, A/V synchronization, and continuous playback pending |
| 0.0.6y | Windows Output Scheduler Adapter Boundary | Implemented; continuous pumping, timer drive, and A/V synchronization pending |
| 0.0.6z | Finite Continuous Playback Pump Policy Boundary | Implemented; precise timing, A/V synchronization, and drift correction pending |
| 0.0.7a | A/V Timing Decision Boundary | Implemented; delay, drop/duplication, resampling, drift correction, and complete A/V synchronization pending |

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
- `src/Nova.Playback/PlaybackDecoderFfmpegAbiSymbolGate.cs`
- `src/Nova.Playback/PortableExecutableDependencyInspector.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegContextLease.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegCodecParameters.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegParameterTransfer.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegCodecOpen.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegPacketFrameBoundary.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegNativeFrameBoundary.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegFrameMetadata.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegFrameDataBoundary.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegManagedFrameCopy.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegDecodedOutputAdapter.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegManagedFormatConversion.cs`
- `src/Nova.Playback/PlaybackDecoderFfmpegAwaitedSinkDelivery.cs`
- `src/Nova.Playback/PlaybackPolicyClockBoundary.cs`
- `src/Nova.Playback/BoundedPlaybackScheduler.cs`
- `src/Nova.Playback/PlaybackWindowsOutputActivationBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputResourceBindingBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputStreamInitializationBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputBufferProcessingBoundary.cs`
- `src/Nova.Playback/PlaybackWindowsOutputSchedulerAdapterBoundary.cs`
- `src/Nova.Playback/FiniteContinuousPlaybackPumpPolicyBoundary.cs`

## 検証入口

- 全体: `build.ps1`
- format: `dotnet format PeerCast-Nova.sln --verify-no-changes`
- lifecycle反復: `tools/Test-DecoderLifecycle.ps1`
- backend反復: `tools/Test-DecoderBackendBoundary.ps1`
- runtime lease反復: `tools/Test-VerifiedRuntimeLease.ps1`
- ABI/symbol gate repetition: `tools/Test-FfmpegAbiSymbolGate.ps1`
- decoder context lease repetition: `tools/Test-FfmpegDecoderContextLease.ps1`
- codec parameter ownership repetition: `tools/Test-FfmpegCodecParameters.ps1`
- native parameter transfer repetition: `tools/Test-FfmpegParameterTransfer.ps1`
- codec-open repetition: `tools/Test-FfmpegCodecOpen.ps1`
- packet/frame repetition: `tools/Test-FfmpegPacketFrameBoundary.ps1`
- callback frame repetition: `tools/Test-FfmpegNativeFrameBoundary.ps1`
- frame metadata repetition: `tools/Test-FfmpegFrameMetadata.ps1`
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
- `docs/adr/ADR-0053-FFMPEG-ABI-SYMBOL-GATE.md`
- `docs/adr/ADR-0054-NATIVE-DECODER-CONTEXT-LEASE.md`
- `docs/adr/ADR-0055-CODEC-PARAMETER-OWNERSHIP-BOUNDARY.md`
- `docs/adr/ADR-0056-NATIVE-PARAMETER-TRANSFER-BOUNDARY.md`
- `docs/adr/ADR-0057-AVCODEC-OPEN-BOUNDARY.md`
- `docs/adr/ADR-0058-PACKET-FRAME-PROCESSING-BOUNDARY.md`
- `docs/adr/ADR-0059-CALLBACK-SCOPED-NATIVE-FRAME-BOUNDARY.md`
- `docs/adr/ADR-0060-NATIVE-FRAME-METADATA-EXTRACTION.md`
- `docs/adr/ADR-0061-CALLBACK-SCOPED-FRAME-DATA-BOUNDARY.md`
- `docs/adr/ADR-0062-MANAGED-FRAME-COPY.md`
- `docs/adr/ADR-0063-DECODED-OUTPUT-ADAPTER.md`
- `docs/adr/ADR-0064-MANAGED-FORMAT-CONVERSION.md`
- `docs/adr/ADR-0065-AWAITED-DECODED-SINK-DELIVERY.md`
- `docs/adr/ADR-0066-PLAYBACK-POLICY-CLOCK-BOUNDARY.md`
- `docs/adr/ADR-0067-BOUNDED-PLAYBACK-SCHEDULER.md`
- `docs/adr/ADR-0070-WINDOWS-OUTPUT-ACTIVATION-BOUNDARY.md`
- `docs/adr/ADR-0071-WINDOWS-OUTPUT-RESOURCE-BINDING-BOUNDARY.md`
- `docs/adr/ADR-0072-WINDOWS-OUTPUT-STREAM-INITIALIZATION-BOUNDARY.md`
- `docs/reviews/PCN-0058-PINNED-FFMPEG-CANDIDATE-VALIDATION.md`
- `docs/reviews/PCN-0060-EXPLICIT-INSTALLED-RUNTIME-LOCATOR-VALIDATION.md`
- `docs/reviews/PCN-0061-VERIFIED-FFMPEG-RUNTIME-LEASE-VALIDATION.md`
- `docs/reviews/PCN-0062-FFMPEG-ABI-SYMBOL-GATE-VALIDATION.md`
- `docs/reviews/PCN-0063-NATIVE-DECODER-CONTEXT-LEASE-VALIDATION.md`
- `docs/reviews/PCN-0064-CODEC-PARAMETER-OWNERSHIP-VALIDATION.md`
- `docs/reviews/PCN-0065-NATIVE-PARAMETER-TRANSFER-VALIDATION.md`
- `docs/reviews/PCN-0066-AVCODEC-OPEN-VALIDATION.md`
- `docs/reviews/PCN-0067-PACKET-FRAME-BOUNDARY-VALIDATION.md`
- `docs/reviews/PCN-0068-CALLBACK-SCOPED-NATIVE-FRAME-VALIDATION.md`
- `docs/reviews/PCN-0069-NATIVE-FRAME-METADATA-VALIDATION.md`
- `docs/reviews/PCN-0070-FRAME-PLANE-SAMPLE-BOUNDARY-VALIDATION.md`
- `docs/reviews/PCN-0071-MANAGED-FRAME-COPY-VALIDATION.md`
- `docs/reviews/PCN-0072-DECODED-OUTPUT-ADAPTER-VALIDATION.md`
- `docs/reviews/PCN-0073-MANAGED-FORMAT-CONVERSION-VALIDATION.md`
- `docs/reviews/PCN-0074-AWAITED-DECODED-SINK-DELIVERY-VALIDATION.md`
- `docs/reviews/PCN-0075-PLAYBACK-POLICY-CLOCK-VALIDATION.md`
- `docs/reviews/PCN-0076-BOUNDED-PLAYBACK-SCHEDULER-VALIDATION.md`
- `docs/reviews/PCN-0080-WINDOWS-OUTPUT-RESOURCE-BINDING-VALIDATION.md`
- `docs/reviews/PCN-0081-WINDOWS-OUTPUT-STREAM-INITIALIZATION-VALIDATION.md`
- `docs/legal/THIRD-PARTY-AND-PROVENANCE.md`

## 次の作業

0.0.6zの次は、有限pump policy上にA/V timing判断を設計します。精密timer、A/V同期、drift補正、demux、
FFmpeg同梱、license approvalは引き続き未完了です。

## まだ完了を主張しないもの

- FFmpeg backend activationまたは配布承認
- FFmpeg同梱判断の法務サインオフ（表示義務の履行含む）
- 実codecによるFLV demux/decode
- decoded frame queue、renderer、audio device
- A/V clockと同期
- audible/visible real-media playback
- GUI、installer、recorder
