# FFmpeg External Runtime Compliance Policy

## Purpose

This document records PeerCast Nova's engineering and release policy for optional use of an external FFmpeg shared runtime while Nova source remains Apache-2.0 and Nova does not redistribute FFmpeg binaries. It is not legal advice.

## Separate the legal obligations from Nova policy

Two layers must not be conflated:

1. **Applicable license obligations.** FFmpeg and its dependencies retain their own licenses. A party that conveys FFmpeg binaries must independently determine and satisfy all obligations applicable to that distribution.
2. **PeerCast Nova policy.** Nova's default project release does not bundle, mirror, rehost, download, extract, install, update, or redistribute FFmpeg. This is a deliberately narrow project rule; it is not a claim that the LGPL always requires this exact architecture.

## Approved architecture class

Nova may use FFmpeg through an external shared-library route only when:

- the runtime is supplied by the user or application host;
- Nova receives one explicit installation directory;
- only the fixed recognizable DLL names are loaded;
- the manifest declares `LGPL-3.0-or-later`;
- GPL, nonfree, unknown, and differently declared classes are rejected;
- required ABI majors and symbols match the reviewed boundary;
- every loaded DLL is verified by exact name, length, and SHA-256;
- no FFmpeg binary enters a Nova source, build, release, installer, or update artifact.

Milestone 0.0.9w implements this boundary through `PlaybackFfmpegExternalRuntimeManifest`, `PlaybackFfmpegExternalRuntimeGenerationFactory`, and `PlaybackFfmpegExternalRuntimeComposition`.

## Authorization classes

### Reviewed built-in candidate

`PlaybackFfmpegExternalRuntimeManifest.BuiltInCandidate` is the only Nova-reviewed identity. Its state is:

- declaration authority: `ProjectReviewed`;
- authorization: `BuiltInApproved`;
- review: `Approved`;
- backend provenance license review: `Approved`.

Its immutable provenance is:

| Field | Value |
| --- | --- |
| Build project | BtbN/FFmpeg-Builds |
| Release tag | `autobuild-2026-07-15-14-01` |
| BtbN commit | `1f74efed63f467dbf0d1e5dd8548bf2188f4ad21` |
| FFmpeg commit | `94138f6973dd1ac6208ace92148ac0d172455d65` |
| Build ID | `btbn.ffmpeg.20260715.win64.lgpl-shared` |
| FFmpeg version | `n8.1.2-22-g94138f6973-20260715` |
| Asset | `ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip` |
| Archive SHA-256 | `F711E0478C03FD48624C59A0487E0C5D904779A3C67280A0A7B49B0002F12B43` |
| Declared license class | `LGPL-3.0-or-later` |
| Distribution | Optional external, not bundled |

Approval applies only to that complete identity and architecture. It does not approve another daily build, a modified build, a host manifest, or redistribution.

### Host-supplied manifest

`CreateHostSupplied` records the host's choices. Its state is:

- declaration authority: `HostDeclared`;
- authorization: `HostAttested`;
- review: `Unreviewed`;
- backend provenance license review: `Pending`.

A host declaration is not proof of the binary's configuration and is not converted into Nova approval. Activation requires all of:

```csharp
new PlaybackDecoderBackendPolicy
{
    AllowThirdParty = true,
    AllowNativeCode = true,
    AllowBundled = false,
    AllowHostAttestedRuntime = true,
}
```

Without the dedicated host-attestation switch, the route returns `PolicyRejected` before runtime directory inspection or native loading.

## Manifest and runtime contract

Both manifest origins carry exactly five fixed records:

| Role | Filename | ABI major |
| --- | --- | ---: |
| AvCodec | `avcodec-62.dll` | 62 |
| AvFormat | `avformat-62.dll` | 62 |
| AvUtil | `avutil-60.dll` | 60 |
| SwResample | `swresample-6.dll` | 6 |
| SwScale | `swscale-9.dll` | 9 |

Each record contains a positive file length and a 64-character SHA-256 digest. The manifest contains no runtime directory, path, URL, archive location, downloader, command, script, installation instruction, update instruction, or fallback location.

Creation fails closed for invalid identities, invalid file counts, undefined or duplicate roles, missing roles, renamed or path-bearing filenames, wrong ABI majors, nonpositive lengths, malformed digests, invalid inventories, and unacceptable declared license classes.

Accepted files pass through:

1. explicit-directory location;
2. retained directory and DLL handles;
3. exact filename, length, and SHA-256 verification;
4. PE dependency validation;
5. ABI-major validation;
6. required-symbol validation;
7. source-bound native session and generation ownership.

Host replacement never disables integrity or native safety gates.

## Provenance evidence

`THIRD-PARTY-LICENSES/FFmpeg/PROVENANCE.md` records the exact release tag, complete BtbN and FFmpeg commits, asset, checksum, and exact upstream variant definitions.

The original 0.0.9w review did not retain verbatim `ffmpeg.exe -buildconf` and `ffmpeg.exe -L` transcripts. The correction records that omission rather than inventing output. Every future built-in candidate update must remain `Pending` until those complete transcripts, archive checksum comparison, extracted inventory, PE dependencies, and dependency-license inventory are retained in the repository.

## Nova binary distribution rules

Nova release engineering must not:

- copy FFmpeg DLLs into a release ZIP;
- embed them as resources;
- include an FFmpeg archive beside an installer;
- mirror or rehost release assets;
- add automatic acquisition to a runtime API;
- discover FFmpeg through `PATH`, registry, system directories, or common install locations;
- rename or obscure FFmpeg DLL identities;
- silently accept GPL or nonfree variants;
- fall back to another runtime after verification failure.

A third party that combines and redistributes Nova with FFmpeg is the FFmpeg binary distributor for that package and must independently satisfy applicable obligations.

## Included legal and release material

`THIRD-PARTY-LICENSES/FFmpeg` contains:

- `NOTICE.md`;
- `RUNTIME-REPLACEMENT.md`;
- `PROVENANCE.md`;
- `NOVA-DISTRIBUTION-GUARD.md`;
- readable `COPYING.LGPLv3` and `COPYING.GPLv3` texts;
- deterministic `COPYING.GPLv3.gz` as a duplicate;
- `SHA256SUMS`.

The fixed text hashes are:

| Text | SHA-256 |
| --- | --- |
| GPLv3 | `3972DC9744F6499F0F9B2DBF76696F2AE7AD8AF9B23DDE66D6AF86C9DFB36986` |
| LGPLv3 | `E3A994D82E644B03A792A930F574002658412F62407F5FEE083F2555C5F23118` |

`Nova.Cli` copies the complete directory to build and publish output. A future graphical application must provide a readily accessible legal/about surface whenever the backend is available.

## Reverse-engineering and trademark guard

Nova-controlled release terms, installer terms, EULAs, and translations must not prohibit reverse engineering performed to debug modifications to the LGPL libraries. `NOVA-DISTRIBUTION-GUARD.md` defines the required release search and fail condition.

FFmpeg is a trademark of Fabrice Bellard, originator of the FFmpeg project. Nova must not imply affiliation or endorsement.

## Candidate lifecycle

A built-in candidate update requires:

1. exact upstream FFmpeg commit;
2. exact build-repository commit;
3. exact release tag, asset, and archive checksum;
4. checksum comparison against upstream release evidence;
5. complete `ffmpeg.exe -buildconf` and `ffmpeg.exe -L` transcripts;
6. extracted file and DLL checksums;
7. PE dependency and dependency-license inventories;
8. GPL/nonfree state verification;
9. ABI and symbol-gate verification;
10. an ADR or explicit amendment and reviewer record.

A floating `latest` URL is never runtime identity. Missing evidence leaves the candidate `Pending`.

## Redistribution escalation gate

Nova may reconsider a separate runtime pack only through a new ADR that supplies the exact corresponding source, build scripts and configuration, patches, notices, source and binary checksums, maintained source location, dependency review, installer/update behavior, and appropriate patent/jurisdiction review. Until that gate is accepted, bundling and rehosting remain prohibited.

## Reviewed public references

- FFmpeg legal and LGPL compliance checklist;
- FFmpeg license description;
- GNU LGPLv3 and GPLv3 texts;
- BtbN/FFmpeg-Builds repository and exact variant files.

See ADR-0132, amended ADR-0133, PCN-0140, and amended PCN-0141.
