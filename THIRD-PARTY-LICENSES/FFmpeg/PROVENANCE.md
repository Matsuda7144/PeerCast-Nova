# Reviewed FFmpeg Candidate Provenance

This record identifies the exact third-party build approved for PeerCast Nova's built-in external-runtime manifest. It is an engineering record, not legal advice.

## Active immutable identity

| Field | Value |
| --- | --- |
| Build repository | `BtbN/FFmpeg-Builds` |
| Release tag | `autobuild-2026-07-21-13-38` |
| Release time | `2026-07-21T13:38:40Z` |
| Fixed asset | `ffmpeg-n8.1.2-29-g703dcc25b9-win64-lgpl-shared-8.1.zip` |
| Fixed asset URL | `https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-07-21-13-38/ffmpeg-n8.1.2-29-g703dcc25b9-win64-lgpl-shared-8.1.zip` |
| Archive size | `70,510,808` bytes |
| Archive SHA-256 | `50764B52D38CB0BAF8AF938C0E4CA886D2C6753B520B2C319B532ED5C17E7CBF` |
| BtbN commit | `8c736b2d6fe5da2a10a8896d01e53bfb0ca4f665` |
| FFmpeg commit | `703dcc25b91eacd2ab8b8b2fe888dc8d7ab4ad6d` |
| FFmpeg version | `n8.1.2-29-g703dcc25b9-20260721` |
| Nova build ID | `btbn.ffmpeg.20260721.win64.lgpl-shared` |
| Build class | `win64 LGPL shared` |
| Expanded inventory | `223` files |

The fixed release tag, asset URL, archive size and digest, both commits, and build ID form one inseparable identity. A floating `latest` URL is not provenance.

## Retained binary evidence

The complete 223-file expanded inventory is retained in `CANDIDATE-20260721-INVENTORY.txt`. The manifest verifies the complete ten-file `bin` directory while only these five roles are eligible for loading:

| File | Size (bytes) | SHA-256 |
| --- | ---: | --- |
| `avcodec-62.dll` | 70,875,136 | `4936D6DD5C1615AC4166017A9843F4D8DC415EF6D3156A4138B84C2A4419818C` |
| `avformat-62.dll` | 22,070,784 | `198B582EE199BA16C973A438FA125A2448772755BC2E95CC01B28AB434270290` |
| `avutil-60.dll` | 2,937,856 | `419513335F9209499243C3C7F2EA46C0B3F477281D80DE90E696E2E1062D7452` |
| `swresample-6.dll` | 723,968 | `344CA8558DCED8431D54E692A171F5167510E940E860292F33CD77460DB0E83B` |
| `swscale-9.dll` | 12,570,624 | `7EBE9B9B4B8B23DA6E86631573E85AE484392CB7B3D0ECF506F621BDC1222229` |

The remaining reviewed `bin` companions are `avdevice-62.dll`, `avfilter-11.dll`, `ffmpeg.exe`, `ffplay.exe`, and `ffprobe.exe`. They are integrity inventory entries, not Nova load roles. No file was removed, relocated, or copied to manufacture an exact inventory.

The verbatim command evidence is retained as:

- `ffmpeg-candidate-buildconf.txt` — complete `ffmpeg.exe -buildconf` output;
- `ffmpeg-candidate-license.txt` — complete `ffmpeg.exe -L` output.

The build configuration contains `--enable-shared` and `--disable-static`; it contains neither `--enable-gpl` nor `--enable-nonfree`. Required exports and ABI majors 62／62／60／6／9 were present.

## PE dependency classification

The reviewed five DLLs may import other verified load-role DLLs and allow-listed Windows system libraries. `d2d1.dll`, `dwrite.dll`, `iphlpapi.dll`, and `usp10.dll` are Windows system dependencies. They are not FFmpeg runtime inventory entries and must not be bundled by Nova or added beside the runtime. `avdevice-62.dll` and `avfilter-11.dll` remain reviewed top-level companion files but are not dependency allow-list entries for the five load roles. Unknown non-system dependencies fail closed.

## Superseded candidate history

The former candidate is retained for rejection and audit history only:

| Field | Superseded value |
| --- | --- |
| Build ID | `btbn.ffmpeg.20260715.win64.lgpl-shared` |
| Release tag | `autobuild-2026-07-15-14-01` |
| Asset | `ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip` |
| BtbN commit | `1f74efed63f467dbf0d1e5dd8548bf2188f4ad21` |
| FFmpeg commit | `94138f6973dd1ac6208ace92148ac0d172455d65` |
| FFmpeg version | `n8.1.2-22-g94138f6973-20260715` |
| Archive SHA-256 | `F711E0478C03FD48624C59A0487E0C5D904779A3C67280A0A7B49B0002F12B43` |

This identity is `Superseded`, not approved for native activation, and its five DLL digests do not satisfy the active manifest.

## Approval scope

`ApprovedForManifestImplementation` applies only to the exact active identity and Nova's explicit-directory, external, dynamically loaded, non-bundled route. It does not approve a modified build, a host-supplied manifest, another daily build, redistribution, native activation, decode, D3D11／WASAPI activation, or audible／visible playback. A host-supplied manifest remains `HostDeclared`, `HostAttested`, and `Unreviewed`.
