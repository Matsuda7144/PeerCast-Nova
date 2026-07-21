# ADR-0137: Approved FFmpeg Candidate Manifest Refresh

## Status

Accepted for milestone 0.0.10a manifest implementation.

## Context

The former built-in candidate `btbn.ffmpeg.20260715.win64.lgpl-shared` no longer matched the currently reviewed BtbN LGPL shared archive. Reusing its manifest for different bytes would bypass provenance and integrity. A new candidate review retained the fixed GitHub release identity, archive digest, complete expanded inventory, command transcripts, PE imports, ABI majors, and required exports before any native load or decode.

The reviewed archive is BtbN release `autobuild-2026-07-21-13-38`, asset `ffmpeg-n8.1.2-29-g703dcc25b9-win64-lgpl-shared-8.1.zip`, size 70,510,808 bytes, SHA-256 `50764B52D38CB0BAF8AF938C0E4CA886D2C6753B520B2C319B532ED5C17E7CBF`. It identifies BtbN commit `8c736b2d6fe5da2a10a8896d01e53bfb0ca4f665` and FFmpeg commit `703dcc25b91eacd2ab8b8b2fe888dc8d7ab4ad6d`.

## Decision

Replace the active built-in identity with `btbn.ffmpeg.20260721.win64.lgpl-shared`. Keep the 2026-07-15 identity and its five hashes as a superseded record so it remains auditable and demonstrably rejected.

The built-in manifest has two related inventories:

1. exactly five load roles: avcodec 62, avformat 62, avutil 60, swresample 6, and swscale 9;
2. the exact ten-file reviewed `bin` inventory, including `avdevice-62.dll`, `avfilter-11.dll`, `ffmpeg.exe`, `ffplay.exe`, and `ffprobe.exe` as integrity-only companions.

Nova loads only the five role DLLs. It does not delete, relocate, or ignore companions to manufacture a smaller runtime. Host-attested replacement manifests retain their exact five-role contract.

Manifest creation records the reviewed build-configuration facts: GPL disabled, nonfree disabled, shared enabled, static disabled. Any conflicting declaration is rejected before filesystem or native access.

Add `d2d1.dll`, `dwrite.dll`, `iphlpapi.dll`, and `usp10.dll` to the Windows system dependency allow-list. These DLLs are Windows components and are neither FFmpeg runtime entries nor Nova distribution payloads. Unknown non-system dependencies remain rejected.

Retain the verbatim `ffmpeg.exe -buildconf` and `ffmpeg.exe -L` outputs and complete 223-file inventory as text-only provenance. FFmpeg archives, executables, and DLLs remain prohibited from source, Git, CI artifacts, build artifacts, publish artifacts, installers, and releases.

## Validation requirements

Focused tests must prove exact archive and five-DLL identities, rejection of superseded and modified bytes, missing／unexpected files and roles, build-configuration rejection, ABI and symbol rejection, acceptance of the four new Windows dependencies, rejection of unknown dependencies, superseded history retention, and binary exclusion. Tests use managed fakes and text evidence only.

## Consequences

The active manifest can now represent the candidate approved by the completed review without weakening host attestation or runtime custody. The ten-file inventory increases the explicit locator ceiling from five to ten for the built-in candidate, while the ABI gate still loads exactly five roles.

This decision does not authorize native DLL loading, runtime ABI or symbol execution, decoder-context creation, packet submission, native decode, D3D11／WASAPI activation, or audible／visible output. Controlled native H.264／AAC decode remains a later gate after this manifest implementation and CI validation.
