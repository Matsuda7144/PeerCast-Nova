# FFmpeg External Runtime Compliance Policy

## Purpose

This document records the PeerCast Nova project policy for using an FFmpeg runtime without changing the Apache License 2.0 license of Nova source code and without redistributing FFmpeg binaries.

It is an engineering compliance record, not legal advice.

## Approved architecture class

Nova may use an FFmpeg runtime only through an external shared-library architecture that satisfies all of the following:

- the runtime is supplied by the user or application host;
- Nova receives one explicit installation directory;
- Nova loads recognizable `avcodec`, `avutil`, `swresample`, `swscale`, and related reviewed DLL names from that directory only;
- the runtime is an LGPLv3 shared build;
- the runtime is not configured with `--enable-gpl`;
- the runtime is not configured with `--enable-nonfree`;
- the runtime major version and required ABI symbols match the reviewed boundary;
- every loaded DLL is verified against an explicit host-owned manifest;
- no FFmpeg binary is included in a Nova source, build, release, installer, or update artifact.

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

The built-in identity is a reviewed convenience manifest. It is not a binary bundled by Nova and is not the only permitted user replacement after the replacement boundary is implemented.

## Upstream build evidence

BtbN documents the following variant split:

- `gpl`: includes GPL-only dependencies;
- `lgpl`: excludes GPL-only dependencies, including libx264 and libx265;
- `nonfree`: includes dependencies that produce a nonfree／unredistributable build;
- `lgpl-shared`: the LGPL dependency set with shared FFmpeg libraries.

The reviewed BtbN scripts currently establish:

```text
defaults-lgpl.sh:
  --enable-version3 --disable-debug
  LICENSE_FILE=COPYING.LGPLv3

defaults-lgpl-shared.sh:
  --enable-shared --disable-static
```

The project must re-review these upstream scripts and the exact candidate build before changing the pinned candidate or accepting a different built-in manifest.

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

## Nova notice obligations

Before an activated FFmpeg-capable Nova binary is published, its distribution must include:

1. a prominent notice that the application uses FFmpeg libraries under GNU LGPLv3;
2. a verbatim copy of GNU LGPLv3;
3. a verbatim copy of GNU GPLv3, which LGPLv3 incorporates;
4. the exact built-in candidate provenance and checksum record;
5. instructions for selecting an explicit runtime directory;
6. instructions for authorizing an interface-compatible modified replacement through a host manifest;
7. a statement that FFmpeg is not licensed under Nova's Apache License 2.0;
8. no term that forbids reverse engineering for debugging modifications to the LGPL library.

A future graphical application must expose the FFmpeg notice and the included license texts through its legal／about surface whenever the FFmpeg backend is available.

## User replacement contract

The current fixed built-in DLL inventory is not by itself the final activated LGPL replacement contract.

The required replacement boundary must accept an explicit host manifest containing:

- a bounded build ID;
- a bounded version label;
- the expected FFmpeg ABI major;
- the required DLL role and exact filename for each library;
- one SHA-256 for each DLL;
- `LGPL-3.0-or-later` as the only accepted license class;
- an explicit host-supplied flag;
- no network location, download instruction, command line, or executable script.

Nova verifies the host manifest and files before loading. The manifest author, not Nova, decides which modified interface-compatible build to trust. This allows replacement without removing checksum verification or implicit-directory safeguards.

The boundary must reject:

- missing or duplicated roles;
- unknown or extra runtime roles;
- invalid filenames or path traversal;
- undefined license identifiers;
- GPL or nonfree declarations;
- wrong ABI major;
- missing files;
- checksum mismatches;
- undefined or missing required symbols;
- runtime directory changes during acquisition;
- cancellation or disposal during verification.

## Backend registration policy

Production FFmpeg is not registered through `PlaybackDecoderBackendFactory`.

The production path requires the application composition root to supply:

- an approved backend policy with third-party and native code enabled;
- bundled code disabled;
- one runtime directory;
- one built-in or host manifest;
- one exact parser-created FLV source session;
- decoder and discontinuity generations;
- one borrowed application-owned Windows output boundary.

This is then passed to the dedicated production generation factory and the finite production PCP／FLV composition.

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
