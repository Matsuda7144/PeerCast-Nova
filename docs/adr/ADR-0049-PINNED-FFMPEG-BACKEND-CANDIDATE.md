# ADR-0049 Pinned FFmpeg Backend Candidate

## Status

Candidate record and technical verification accepted in 0.0.6b. Activation is
not approved.

## Decision

The first real-decoder candidate is the BtbN Windows x64 LGPL shared build from
the FFmpeg 8.1 release branch, built 2026-07-15. The candidate is pinned by
filename, FFmpeg version, configuration evidence, and archive SHA-256.

The candidate declares third-party optional external native provenance with
license review `Pending`. Even a permissive host policy therefore rejects it.
No archive, executable, DLL, header, or license file is bundled with Nova.

## Candidate

- Asset: `ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip`
- GitHub asset ID: `478022771`
- Published: `2026-07-15T14:26:00Z`
- FFmpeg: `n8.1.2-22-g94138f6973-20260715`
- SHA-256: `F711E0478C03FD48624C59A0487E0C5D904779A3C67280A0A7B49B0002F12B43`
- Linking: shared enabled, static disabled
- License mode: version 3 enabled; bundled text is LGPLv3
- GPL/nonfree configure flags: absent
- FLV demuxers: `flv` and `live_flv` present

## Remaining approval

ADR-0051 resolves the distribution choice in favor of a user/host-installed
runtime with dynamic linking and no Nova redistribution. Before activation,
record the corresponding FFmpeg and dependency sources, complete the remaining
LGPLv3 product-display and license-access review, and review every enabled
external library.
