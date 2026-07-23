# PeerCast Nova Navigation

## Phase 5 corrected physical video validation

The corrected physical video run completed once from clean main `cd7442e18d18acfe2faf43470ef1c97f77ec513f` with the approved external runtime and fixed retained fixture. The canonical record reported `Completed／Accepted`, exactly one delivery, zero retained WouldBlock, stopped runner, disposed lifecycle, preserved borrowed owners, and no external publication. Verify-only single-record preflight returned `VideoAccepted`, and the operator recorded `Video visible: Yes` for recognizable fixture content.

- `tools/Run-ControlledWindowsPhysicalVideo.ps1`
- `docs/runbooks/0.0.10d-controlled-windows-video-revalidation.md`

## Phase 5 video-gated controlled audio run

The accepted visible video record now opens the separately gated audio run. The dedicated Windows helper first requires that exact external record to pass `VideoAccepted`, then executes one physical `audio` run, keeps the accepted WASAPI buffer alive for the default one-second observation interval, retains one external canonical audio record, and requires `AudioAccepted`. It performs no paired verification.

The first audio attempt on clean main
`36ff2d616965975f9d42879cfbf4e23ded99ace1` passed `VideoAccepted` but its
single delivery attempt ended `DeliveryRejected` with zero delivered, zero
retained WouldBlock, and no native audio evidence. Audio preflight and operator
observation were not run; WASAPI reachability remains unproven. The cause was the
generation scheduler's video-first initial reservation conflicting with the
audio-only controlled run while both media kinds were queued. The corrected
controlled lifecycle maps its requested media kind to the existing bounded
scheduler's first successful reservation. Normal production remains video-first
and `DropIncoming`; controlled capacity `16`, `64 MiB`, and `DropOldest` are
unchanged. Corrected physical audio and paired verification remain pending.

The first corrected-selection audio attempt on clean main
`e8b7fcac2f679eb5023ae8ab36571b86773d3950` passed the video gate but
produced zero child stdout bytes and no canonical audio record. The helper exited
`2` and left a zero-byte external audio-record file. Counters, states, and owner
preservation are unknown; audio preflight, audible observation, and paired
verification were not executed. No retry or fallback occurred. Because the old
helper lost the child exit code and stderr boundary, no WASAPI call or crash code
is established, and the result does not refute PR #112.

The controlled diagnostic boundary now promotes only complete canonical stdout
and otherwise writes a separate repository-external sanitized failure record.
The dedicated explicit flag reports only fixed native audio stage names; raw
stderr, paths, device identities, pointers, handles, HRESULTs, and media data are
not recorded. The next physical audio command is diagnostic only and remains
blocked from final acceptance and paired verification.

The first diagnostic invocation from main
`4968e40f801f72bb06a71007a04a47911b6ac377` built the Release runner but
failed inside the Windows PowerShell 5.1 helper before physical-child start.
Neither record was created; physical output, preflight, audible observation, and
paired verification were not run. The corrected boundary keeps the documented
`powershell.exe -NoProfile -ExecutionPolicy Bypass` host while moving child
process construction, concurrent byte capture, diagnostics, and atomic record
promotion into the `net10.0`
`--capture-controlled-windows-audio-child` command. PowerShell 7 is not required,
and this host correction adds no WASAPI or final acceptance evidence.

- `docs/adr/ADR-0148-video-gated-controlled-audio-run.md`
- `docs/reviews/PCN-0156-VIDEO-GATED-CONTROLLED-AUDIO-RUN.md`
- `src/Nova.Cli/PlaybackControlledWindowsAudioChildCaptureCommand.cs`
- `tools/Run-ControlledWindowsPhysicalAudio.ps1`
- `docs/runbooks/0.0.10d-controlled-windows-audio-validation.md`

The later diagnostic run on clean main
`e7e82b01a1eb323954b8ef2375b025064a796a8d` passed the video gate and audio
preflight and produced a `Completed` / `Accepted` record with one delivery,
zero WouldBlock, stopped/disposed lifecycle, and preserved borrowed owners.
The operator recorded `Audio audible: No`: only a short noise-like sound was
heard, not recognizable fixture audio. Deterministic evidence remains valid,
recognizable physical audio remains incomplete, and paired verification is
blocked.

## Phase 5 controlled known-audio tone probe

The diagnostic-only probe generates one fixed managed 80 ms, 1,000 Hz,
48 kHz stereo float tone, submits exactly one production audio-buffer write,
requires one ReleaseBuffer and Start acknowledgement, holds without additional
output, and performs production-first cleanup. It bypasses FFmpeg, fixture,
parser, decoder, scheduler, timing, physical run records, and video Present.
The canonical path-free record distinguishes a recognizable beep from a
canonical PCM/WASAPI interpretation failure without claiming final physical
audio acceptance.

- `docs/adr/ADR-0149-controlled-known-audio-tone-probe.md`
- `docs/reviews/PCN-0157-CONTROLLED-KNOWN-AUDIO-TONE-PROBE.md`
- `src/Nova.Cli/PlaybackControlledWindowsKnownAudioToneProbe.cs`
- `tools/Run-ControlledWindowsKnownAudioToneProbe.ps1`

The separately approved physical probe on clean main
`2ca34405b8bdd81611aa2414092c91a95a8d6d51` completed with accepted evidence,
one acknowledged write and Start, stopped/disposed cleanup, preserved borrowed
owners, and operator observation `Known audio tone audible: Yes`. The 80 ms
1,000 Hz beep was recognizable. Canonical Float32 production WASAPI output is
therefore operational; paired verification remains blocked while fixture audio
assembly is isolated.

## Phase 5 controlled decoded-audio slice probe

This diagnostic reuses the approved runtime, retained fixture, existing FLV
demux/native AAC decode, and managed conversion. It copies the chronological
first 80 ms as three complete 1,024-sample frames plus 768 samples from the
fourth, yielding one private 3,840-frame / 30,720-byte canonical Float32 stereo
buffer. Only the complete aggregate reaches one production audio write. The
scheduler and normal one-frame composition are bypassed but unchanged.

- `docs/adr/ADR-0152-controlled-decoded-audio-slice-probe.md`
- `docs/reviews/PCN-0160-CONTROLLED-DECODED-AUDIO-SLICE-PROBE.md`
- `docs/runbooks/0.0.10d-controlled-decoded-audio-slice-probe.md`
- `src/Nova.Playback/PlaybackDecodedAudioSliceCollector.cs`
- `src/Nova.Cli/PlaybackControlledWindowsDecodedAudioSliceProbe.cs`
- `tools/Run-ControlledWindowsDecodedAudioSliceProbe.ps1`

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
