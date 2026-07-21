# FFmpeg External Runtime Compliance Policy

## Purpose

This document records the PeerCast Nova project policy and implemented engineering boundary for using an FFmpeg runtime without changing the Apache License 2.0 license of Nova source code and without redistributing FFmpeg binaries.

It is an engineering compliance record, not legal advice.

## Approved architecture class

Nova may use an FFmpeg runtime only through an external shared-library architecture that satisfies all of the following:

- the runtime is supplied by the user or application host;
- Nova receives one explicit installation directory;
- Nova loads only the reviewed recognizable shared DLL names from that directory;
- the runtime is declared `LGPL-3.0-or-later`;
- GPL and nonfree configurations are rejected;
- the runtime major version and required ABI symbols match the reviewed boundary;
- every loaded DLL is verified against one immutable built-in or host-owned manifest;
- no FFmpeg binary is included in a Nova source, build, release, installer, or update artifact.

Milestone 0.0.9w implements this architecture through `PlaybackFfmpegExternalRuntimeManifest`, `PlaybackFfmpegExternalRuntimeGenerationFactory`, and `PlaybackFfmpegExternalRuntimeComposition`.

## Reviewed built-in candidate

The built-in technical candidate is:

| Field | Value |
| --- | --- |
| Build project | BtbN/FFmpeg-Builds |
| Build ID | `btbn.ffmpeg.20260715.win64.lgpl-shared` |
| FFmpeg version | `n8.1.2-22-g94138f6973-20260715` |
| Asset | `ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip` |
| Archive SHA-256 | `F711E0478C03FD48624C59A0487E0C5D904779A3C67280A0A7B49B0002F12B43` |
| License class | `LGPL-3.0-or-later` |
| Distribution | Optional external, host supplied |
| Linking | Shared DLL runtime loading |
| Runtime review | `Approved` |

The built-in identity is a reviewed convenience manifest. It is not a binary bundled by Nova and is not the only permitted user replacement.

## Upstream build evidence

BtbN documents the following variant split:

- `gpl`: includes GPL-only dependencies;
- `lgpl`: excludes GPL-only dependencies, including libx264 and libx265;
- `nonfree`: includes dependencies that produce a nonfree／unredistributable build;
- `lgpl-shared`: the LGPL dependency set with shared FFmpeg libraries.

The reviewed BtbN scripts establish:

```text
defaults-lgpl.sh:
  --enable-version3 --disable-debug
  LICENSE_FILE=COPYING.LGPLv3

defaults-lgpl-shared.sh:
  --enable-shared --disable-static
```

The project must re-review these upstream scripts and the exact candidate build before changing the pinned candidate or accepting a different built-in manifest.

## Implemented manifest contract

`PlaybackFfmpegExternalRuntimeManifest` has one fixed origin:

- `BuiltInCandidate`; or
- `HostSupplied`.

It accepts exactly five records:

| Role | Filename | ABI major |
| --- | --- | ---: |
| AvCodec | `avcodec-62.dll` | 62 |
| AvFormat | `avformat-62.dll` | 62 |
| AvUtil | `avutil-60.dll` | 60 |
| SwResample | `swresample-6.dll` | 6 |
| SwScale | `swscale-9.dll` | 9 |

Each record includes a positive file length and one SHA-256 digest. The manifest accepts only the exact license identifier `LGPL-3.0-or-later`.

The manifest contains no runtime directory, path, URL, archive location, command, script, download, extraction, installation, update, or fallback instruction.

Manifest creation fails closed for invalid identities, undefined／duplicate／missing roles, renamed or path-bearing filenames, wrong ABI majors, nonpositive lengths, malformed digests, invalid inventory, GPL, nonfree, LGPL-2.1, and unknown license declarations.

## User replacement contract

A user or application host can authorize an interface-compatible modified LGPL build through:

```text
PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied(...)
```

The host manifest supplies:

- a bounded build ID;
- a bounded version label;
- `LGPL-3.0-or-later`;
- the five fixed roles, filenames, and ABI majors;
- the exact length and SHA-256 for each selected DLL.

The host, not Nova, selects the trusted modified build. Nova converts the accepted records into the existing runtime inventory and verifies the exact files before native loading.

Replacement support does not disable integrity verification. A byte change after manifest creation produces a digest mismatch.

## Runtime and production registration

Production FFmpeg is not registered through `PlaybackDecoderBackendFactory`.

The application composition root supplies:

- an approved backend policy with third-party and native code enabled;
- bundled code disabled;
- one explicit runtime directory;
- one built-in or host manifest;
- one exact parser-created FLV source session;
- decoder and discontinuity generations;
- one borrowed application-owned Windows output boundary.

`PlaybackFfmpegExternalRuntimeGenerationFactory` passes the selected manifest to the existing production generation factory. Runtime acquisition continues through:

1. explicit-directory location;
2. retained directory and DLL handles;
3. exact filename, length, and SHA-256 verification;
4. PE dependency validation;
5. reviewed ABI-major validation;
6. required-symbol validation;
7. source-bound native session and generation ownership.

`PlaybackFfmpegExternalRuntimeComposition.Create` constructs the existing finite production PCP／FLV composition. It adds no discovery, network access, download, installation, fallback, resident loop, second queue, or Windows resource ownership.

## Nova binary distribution rules

Nova release engineering must not:

- copy FFmpeg DLLs into a release ZIP;
- embed FFmpeg DLLs as resources;
- include an FFmpeg archive beside the installer;
- mirror or rehost BtbN release assets;
- add a download URL to a runtime API contract;
- download or extract FFmpeg automatically;
- discover FFmpeg through `PATH`, registry, system directory, or common install location;
- rename or obscure FFmpeg DLL identities;
- silently accept GPL or nonfree variants;
- fall back to an unreviewed runtime after verification failure.

A third party that combines and redistributes Nova with FFmpeg binaries is acting as the FFmpeg binary distributor and must satisfy the applicable source, build, notice, and license obligations independently.

## Included legal material

`THIRD-PARTY-LICENSES/FFmpeg` contains:

- `NOTICE.md`;
- `RUNTIME-REPLACEMENT.md`;
- verbatim `COPYING.LGPLv3`;
- deterministic `COPYING.GPLv3.gz` and its extraction README;
- `SHA256SUMS`.

The decoded text hashes are fixed by tests:

| Text | SHA-256 |
| --- | --- |
| GPLv3 | `3972DC9744F6499F0F9B2DBF76696F2AE7AD8AF9B23DDE66D6AF86C9DFB36986` |
| LGPLv3 | `E3A994D82E644B03A792A930F574002658412F62407F5FEE083F2555C5F23118` |

The notice explains that FFmpeg is external LGPL software, is not licensed under Nova's Apache License 2.0, is not bundled by Nova, and may be replaced through the documented host manifest route. Nova terms do not prohibit reverse engineering performed to debug modifications to the LGPL library.

`Nova.Cli` copies the complete legal directory to build and publish output.

A future graphical application must expose the FFmpeg notice and the included license texts through its legal／about surface whenever the FFmpeg backend is available.

## Candidate lifecycle

A built-in candidate update requires:

1. exact upstream FFmpeg commit;
2. exact BtbN build repository commit or equivalent reproducible build definition;
3. exact asset identity and archive checksum;
4. exact extracted DLL inventory and checksums;
5. re-verification of LGPL／GPL／nonfree configure state;
6. ABI symbol-gate verification;
7. retained-fixture validation;
8. an ADR or explicit amendment to the current candidate record.

A floating `latest` URL is never runtime identity. Daily third-party asset retention is not part of Nova's availability guarantee.

## Redistribution escalation gate

Nova may reconsider a separate runtime pack only through a new ADR that supplies, at minimum:

- the exact FFmpeg and dependency corresponding source;
- complete build scripts and configure output;
- all patches and a changes file;
- all required copyright and license notices;
- source and binary checksums;
- a source location maintained for the required period;
- review of every statically or dynamically linked dependency;
- installer and update compliance behavior;
- patent and jurisdiction review appropriate to the intended distribution.

Until that gate is accepted, bundling and rehosting remain prohibited.

## Sources reviewed on 2026-07-21

- FFmpeg legal and LGPL compliance checklist: `https://ffmpeg.org/legal.html`
- FFmpeg current license description: `https://ffmpeg.org/doxygen/trunk/md_LICENSE.html`
- GNU LGPLv3: `https://www.gnu.org/licenses/lgpl-3.0.html`
- GNU license FAQ, dynamic linking and source conveyance: `https://www.gnu.org/licenses/gpl-faq.html#LGPLStaticVsDynamic`
- BtbN build repository and variant descriptions: `https://github.com/BtbN/FFmpeg-Builds`
- BtbN LGPL defaults: `https://github.com/BtbN/FFmpeg-Builds/blob/master/variants/defaults-lgpl.sh`
- BtbN LGPL shared defaults: `https://github.com/BtbN/FFmpeg-Builds/blob/master/variants/defaults-lgpl-shared.sh`
- BtbN Windows x64 LGPL shared composition: `https://github.com/BtbN/FFmpeg-Builds/blob/master/variants/win64-lgpl-shared.sh`

See ADR-0132, ADR-0133, PCN-0140, and PCN-0141.