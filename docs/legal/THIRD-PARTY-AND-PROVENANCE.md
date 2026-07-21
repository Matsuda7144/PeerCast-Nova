# Third-Party Code and Protocol Provenance

## Project license

PeerCast Nova source code is licensed under Apache License 2.0.

## PeerCastStation

PeerCastStation is used as an interoperability reference and test peer. Its source repository uses a GPL-compatible contribution policy.

**Decision:** PeerCast Nova does not copy or translate PeerCastStation source code into the Apache-licensed codebase.

## Independent PCP implementation references

Public PCP implementations may be consulted to identify behavior worth testing. The current `Nova.Protocol.Pcp` code was written independently from documented wire observations and contains no copied third-party implementation code.

If code is later copied, translated, or substantially derived from another project, the contributor must:

1. verify license compatibility;
2. preserve required notices;
3. document the exact files and origin;
4. update third-party notices before merge.

## Visual assets

The Apache License covering Nova code does not automatically grant rights to the PeerCast name, logo, icons, or other inherited visual assets. Before public distribution, the project must establish and document permission for every reused asset. Until then, use text-only placeholders or newly created assets.

## Decoder backend boundary

Milestone 0.0.5c defines only original Apache-2.0 lifecycle interfaces, ownership code, and test fakes. It does not include FFmpeg, GStreamer, VLC, Media Foundation wrappers, codec binaries, headers, generated bindings, or copied decoder source.

Milestone 0.0.5d adds only an original CLI-internal counting decoder and content-free diagnostic composition. It does not decode, inspect, retain, or transform media payload and does not change the backend licensing boundary.

Before adding a real backend, the change must record:

1. exact upstream project, version, source or binary origin, and checksum;
2. license for every linked or redistributed component;
3. static, dynamic, or runtime-loading behavior and build flags;
4. required copyright, attribution, source, relinking, replacement, and notice material;
5. patent-encumbered, GPL, or nonfree components;
6. assembly/package boundaries preventing backend types from entering Network, Core, or UI contracts;
7. behavior when the backend is absent.

A strong-copyleft backend or configuration must not become an undocumented part of the default Apache-2.0 distribution. LGPL configurations require an explicit compliance plan. Operating-system media APIs still require documented SDK/runtime terms and redistribution assumptions. Legal review is required before publishing a package that bundles a third-party decoder.

Milestone 0.0.5i enforces generic provenance policy in code. Every backend descriptor declares origin, distribution, license-review state, and native-code use. The generic policy accepts only `Approved` provenance; pending or rejected review cannot be enabled by ordinary policy switches.

Milestone 0.0.5k requires third-party, bundled, and native backend descriptors to carry a bounded artifact manifest with upstream identity, version, license identifier, and SHA-256. Paths and URLs remain outside the runtime contract.

Milestones 0.0.6a through 0.0.6c add exact candidate and DLL integrity evidence without bundling or approving redistribution.

ADR-0051 fixes the default distribution policy: Nova does not bundle or redistribute FFmpeg. A user or host supplies one explicit directory, and Nova dynamically loads only verified libraries. Nova does not search `PATH`, registry, or conventional locations.

ADR-0052 retains verified DLL and directory handles through the next gate. This integrity boundary does not change distribution policy.

## FFmpeg external-runtime decision

Milestone 0.0.9v selects the exact BtbN `win64-lgpl-shared` build class for external dynamic use. Nova's no-bundling architecture is a project policy and must not be represented as the complete set of LGPL obligations.

The permanent default distribution decision is:

- Nova does not bundle, mirror, rehost, download, extract, update, install, or redistribute FFmpeg binaries;
- the user or application host supplies one explicit runtime directory;
- GPL, nonfree, unknown, and differently declared variants remain prohibited;
- production registration occurs at the application composition root, not through the generic backend factory;
- a third party redistributing FFmpeg with Nova is independently responsible for its binary/source compliance package.

## Corrected 0.0.9w authorization model

`PlaybackFfmpegExternalRuntimeManifest` has two origins but not two Nova approvals.

### Built-in candidate

The exact built-in identity is:

- BtbN release tag `autobuild-2026-07-15-14-01`;
- BtbN commit `1f74efed63f467dbf0d1e5dd8548bf2188f4ad21`;
- FFmpeg commit `94138f6973dd1ac6208ace92148ac0d172455d65`;
- archive SHA-256 `F711E0478C03FD48624C59A0487E0C5D904779A3C67280A0A7B49B0002F12B43`.

Only this identity is `ProjectReviewed`, `BuiltInApproved`, and `Approved` for the external-only route.

### Host-supplied manifest

A host manifest is `HostDeclared`, `HostAttested`, and `Unreviewed`; its backend license-review value remains `Pending`. The exact files are still bound by length, SHA-256, filenames, ABI majors, dependencies, and symbols.

Host activation requires `AllowHostAttestedRuntime = true` in addition to explicit third-party/native permission and `AllowBundled = false`. This dedicated switch is an application acceptance of host responsibility, not a Nova approval of an arbitrary build.

The generic backend policy still rejects pending provenance. The host-specific route evaluates the explicit attestation before any runtime path or native operation and then reuses the reviewed FFmpeg ABI/backend class gates.

## Evidence correction

`THIRD-PARTY-LICENSES/FFmpeg/PROVENANCE.md` records exact release and commit identity plus the upstream variant definitions. It also records that the original 0.0.9w review did not retain verbatim `ffmpeg.exe -buildconf` and `ffmpeg.exe -L` transcripts. Those transcripts must not be reconstructed or invented.

Every future built-in candidate remains `Pending` until complete command transcripts, archive checksum comparison, extracted inventory, PE dependencies, and dependency-license inventory are committed.

## Legal material and release guard

Activated CLI build and publish output includes:

- prominent notice and replacement instructions;
- exact provenance record;
- readable GPLv3 and LGPLv3 texts;
- deterministic compressed GPLv3 duplicate;
- fixed license-text hashes;
- a distribution-terms guard.

Nova release, installer, EULA, and translated terms must not prohibit reverse engineering performed to debug modifications to LGPL libraries. FFmpeg trademark attribution is retained, and Nova must not imply affiliation or endorsement.

See `FFMPEG-EXTERNAL-RUNTIME-COMPLIANCE.md`, ADR-0132, amended ADR-0133, PCN-0140, and amended PCN-0141.
