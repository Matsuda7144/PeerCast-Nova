# PCN-0145: Approved FFmpeg Candidate Manifest Refresh Validation

## Milestone

0.0.10a

## Review result

`ApprovedForManifestImplementation`

## Evidence accepted

- fixed release tag `autobuild-2026-07-21-13-38` and release time `2026-07-21T13:38:40Z`;
- fixed asset name and release URL, not a floating latest URL;
- archive size 70,510,808 bytes and SHA-256 `50764B52D38CB0BAF8AF938C0E4CA886D2C6753B520B2C319B532ED5C17E7CBF`;
- BtbN commit `8c736b2d6fe5da2a10a8896d01e53bfb0ca4f665`;
- FFmpeg commit `703dcc25b91eacd2ab8b8b2fe888dc8d7ab4ad6d` and version `n8.1.2-29-g703dcc25b9-20260721`;
- complete 223-file expanded inventory and exact ten-file `bin` inventory;
- verbatim `-buildconf` and `-L` transcripts;
- GPL and nonfree disabled, shared enabled, static disabled;
- ABI majors 62／62／60／6／9 and all required exports present;
- read-only PE imports and dependency classification.

## Exact load roles

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| `avcodec-62.dll` | 70,875,136 | `4936D6DD5C1615AC4166017A9843F4D8DC415EF6D3156A4138B84C2A4419818C` |
| `avformat-62.dll` | 22,070,784 | `198B582EE199BA16C973A438FA125A2448772755BC2E95CC01B28AB434270290` |
| `avutil-60.dll` | 2,937,856 | `419513335F9209499243C3C7F2EA46C0B3F477281D80DE90E696E2E1062D7452` |
| `swresample-6.dll` | 723,968 | `344CA8558DCED8431D54E692A171F5167510E940E860292F33CD77460DB0E83B` |
| `swscale-9.dll` | 12,570,624 | `7EBE9B9B4B8B23DA6E86631573E85AE484392CB7B3D0ECF506F621BDC1222229` |

## Dependency decision

`d2d1.dll`, `dwrite.dll`, `iphlpapi.dll`, and `usp10.dll` are accepted as Windows system dependencies. They must not be supplied by Nova or copied into the reviewed runtime. Existing verified runtime-role imports remain accepted; unknown non-system imports remain rejected.

## Superseded history

`btbn.ffmpeg.20260715.win64.lgpl-shared` remains in code and provenance as superseded. Its release, commits, archive digest, and five DLL digests are preserved. The active manifest does not accept those bytes.

## Controlled native decode plan

After this implementation is merged and all CI gates pass, a separate controlled Windows validation may:

1. use one explicit external runtime directory whose exact ten-file inventory matches the active manifest;
2. use the retained controlled FLV fixture with fixed provenance and digest;
3. record locator verification, dependency inspection, ABI major, required symbol, H.264／AAC context creation, finite packet submission, frame receive, metadata, byte bounds, and cleanup;
4. keep all runtime and decoded evidence outside repository and artifacts;
5. stop on the first mismatch with no PATH, registry, fallback, download, or alternate candidate search.

That later validation must not activate D3D11／WASAPI, play audio, display video, or redistribute FFmpeg.

## Scope confirmation

This milestone changes manifest, policy evidence, tests, and documentation only. No native DLL was loaded; no runtime ABI or symbol function was invoked; no decoder context was created; no packet was sent; and no native decode or physical output activation occurred.
