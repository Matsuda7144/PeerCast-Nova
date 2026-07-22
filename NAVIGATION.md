# PeerCast Nova Navigation

## Phase 5 corrected physical video validation

The corrected physical video run completed once from clean main `cd7442e18d18acfe2faf43470ef1c97f77ec513f` with the approved external runtime and fixed retained fixture. The canonical record reported `Completed／Accepted`, exactly one delivery, zero retained WouldBlock, stopped runner, disposed lifecycle, preserved borrowed owners, and no external publication. Verify-only single-record preflight returned `VideoAccepted`, and the operator recorded `Video visible: Yes` for recognizable fixture content.

- `tools/Run-ControlledWindowsPhysicalVideo.ps1`
- `docs/runbooks/0.0.10d-controlled-windows-video-revalidation.md`

## Phase 5 video-gated controlled audio run

The accepted visible video record now opens the separately gated audio run. The dedicated Windows helper first requires that exact external record to pass `VideoAccepted`, then executes one physical `audio` run, keeps the accepted WASAPI buffer alive for the default one-second observation interval, retains one external canonical audio record, and requires `AudioAccepted`. It performs no paired verification.

- `docs/adr/ADR-0148-video-gated-controlled-audio-run.md`
- `docs/reviews/PCN-0156-VIDEO-GATED-CONTROLLED-AUDIO-RUN.md`
- `tools/Run-ControlledWindowsPhysicalAudio.ps1`
- `docs/runbooks/0.0.10d-controlled-windows-audio-validation.md`

## Phase 5 corrected Windows output probes

The private `ID3D11DeviceContext` declarations omitted `OMSetBlendState` and `OMSetDepthStencilState`, shifting every later COM vtable call by two slots. The correction restored the official method order and native `void` signatures.

On main `5386637fdca7cab8dfd451491f99422930a67bfb`, both the diagnostic `CreateTexture2D+CopyResource` path and the production direct `UpdateSubresource` path completed with one Present acknowledgement, exact accepted bytes, complete cleanup, and visible red／green／blue／white quadrants.

- `docs/adr/ADR-0150-controlled-known-pattern-windows-output-probe.md`
- `docs/reviews/PCN-0158-CONTROLLED-KNOWN-PATTERN-WINDOWS-OUTPUT-PROBE.md`
- `docs/runbooks/0.0.10d-controlled-windows-known-pattern-probe.md`
- `tools/Run-ControlledWindowsKnownPatternProbe.ps1`
- `docs/adr/ADR-0151-controlled-d3d11-texture-copy-output-probe.md`
- `docs/reviews/PCN-0159-CONTROLLED-D3D11-TEXTURE-COPY-OUTPUT-PROBE.md`
- `docs/runbooks/0.0.10d-controlled-windows-texture-copy-probe.md`
- `tools/Run-ControlledWindowsTextureCopyProbe.ps1`
- `src/Nova.Playback/PlaybackWindowsOutputTextureCopyProbeBoundary.cs`
- `tests/Nova.Playback.Tests/PlaybackWindowsD3D11ComSignatureTests.cs`

## Phase 5 single physical run record preflight

A verify-only command reads one repository-external canonical physical record through the same bounded UTF-8 reader and submits it to the independent run-set admission boundary used by paired verification. It starts no runner, runtime, fixture, decode, or physical output and never prints the record path.

- `docs/adr/ADR-0147-single-physical-run-record-preflight.md`
- `docs/reviews/PCN-0155-SINGLE-PHYSICAL-RUN-RECORD-PREFLIGHT.md`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunRecordPreflightCommand.cs`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunRecordFile.cs`
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsPhysicalRunRecordPreflightCommandTests.cs`

## Phase 5 bounded physical output observation hold

The controlled physical-run composition keeps an already delivered item and its existing output lifecycle alive for one bounded operator-observation interval before evidence capture and cleanup. No decode, scheduler delivery, retry, resubmission, native output operation, timestamp change, or evidence mutation occurs during the hold.

- `docs/adr/ADR-0146-bounded-physical-output-observation-hold.md`
- `docs/reviews/PCN-0154-BOUNDED-PHYSICAL-OUTPUT-OBSERVATION-HOLD.md`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunComposition.cs`
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsPhysicalObservationHoldTests.cs`

## Phase 5 production timing and decoded timestamps

The bounded FLV decoder configures the exact parser stream time base through public FFmpeg APIs. The production timing path anchors the first valid decoded presentation timestamp to the first monotonic playback-position read and preserves later source-relative offsets. Raw timestamps are not rewritten, tolerances are unchanged, and no retry or fallback path is added.

- `docs/adr/ADR-0144-explicit-decoder-packet-time-base-propagation.md`
- `docs/reviews/PCN-0152-DECODED-TIMESTAMP-PROPAGATION.md`
- `src/Nova.Playback/PlaybackDecoderFfmpegPacketTimeBaseConfiguration.cs`
- `docs/adr/ADR-0145-production-timing-presentation-origin.md`
- `docs/reviews/PCN-0153-PRODUCTION-TIMING-PRESENTATION-ORIGIN.md`
- `src/Nova.Playback/PlaybackAvTimingDecisionBoundary.cs`

## 現在地

**0.0.10d — Controlled Physical Windows Video／Audio Output Validation — Phase 5 Physical Audio Validation Ready**

- corrected D3D11 texture-copy pattern: deterministic success, visible `Yes`;
- corrected production `UpdateSubresource` pattern: deterministic success, visible `Yes`;
- corrected decoded physical video: `Completed／Accepted`, `VideoAccepted`, visible `Yes`;
- approved runtime: `btbn.ffmpeg.20260721.win64.lgpl-shared`;
- fixture: `66,939` bytes, SHA-256 `32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15`;
- next gate: one video-gated controlled physical audio run with a new external audio record;
- paired verification remains blocked until the audio record is `AudioAccepted` and audible observation is recorded separately;
- no runtime path, fixture path, record path, device／endpoint identity, HWND, thread ID, pointer, handle, or payload is retained in the repository.

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終headでrestore、Release build with analyzers、full solution tests、format verificationを通してからmergeします。

## 最新の入口

- `docs/runbooks/0.0.10d-controlled-windows-audio-validation.md`
- `tools/Run-ControlledWindowsPhysicalAudio.ps1`
- `docs/adr/ADR-0148-video-gated-controlled-audio-run.md`
- `docs/reviews/PCN-0156-VIDEO-GATED-CONTROLLED-AUDIO-RUN.md`
- `docs/runbooks/0.0.10d-controlled-windows-video-revalidation.md`
- `tools/Run-ControlledWindowsPhysicalVideo.ps1`
- `docs/adr/ADR-0150-controlled-known-pattern-windows-output-probe.md`
- `docs/reviews/PCN-0158-CONTROLLED-KNOWN-PATTERN-WINDOWS-OUTPUT-PROBE.md`
- `docs/adr/ADR-0151-controlled-d3d11-texture-copy-output-probe.md`
- `docs/reviews/PCN-0159-CONTROLLED-D3D11-TEXTURE-COPY-OUTPUT-PROBE.md`
- `tests/Nova.Playback.Tests/PlaybackWindowsD3D11ComSignatureTests.cs`
