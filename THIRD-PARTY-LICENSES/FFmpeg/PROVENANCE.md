# Reviewed FFmpeg Candidate Provenance

This record identifies the exact third-party build reviewed for PeerCast Nova's built-in external-runtime manifest. It is an engineering record, not legal advice.

## Immutable identity

| Field | Value |
| --- | --- |
| Build repository | `BtbN/FFmpeg-Builds` |
| Release tag | `autobuild-2026-07-15-14-01` |
| Release time | 2026-07-15 14:02 UTC |
| BtbN commit | `1f74efed63f467dbf0d1e5dd8548bf2188f4ad21` |
| FFmpeg commit | `94138f6973dd1ac6208ace92148ac0d172455d65` |
| FFmpeg version label | `n8.1.2-22-g94138f6973-20260715` |
| Asset | `ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip` |
| Archive SHA-256 | `F711E0478C03FD48624C59A0487E0C5D904779A3C67280A0A7B49B0002F12B43` |
| Nova build ID | `btbn.ffmpeg.20260715.win64.lgpl-shared` |

The release tag, both complete commits, asset name, and archive checksum are one inseparable identity. A floating `latest` URL is not evidence of the reviewed bytes.

## Exact upstream build-definition evidence

At BtbN commit `1f74efed63f467dbf0d1e5dd8548bf2188f4ad21`:

- `variants/win64-lgpl-shared.sh` selects `windows-install-shared.sh` and `defaults-lgpl-shared.sh`;
- `variants/defaults-lgpl-shared.sh` selects `defaults-lgpl.sh`, then adds `--enable-shared --disable-static`;
- `variants/defaults-lgpl.sh` sets `--enable-version3 --disable-debug`, selects FFmpeg `master`, and packages `COPYING.LGPLv3`;
- the LGPL variant excludes the GPL-only dependency set used by the GPL variant;
- the nonfree variant is a separate build class and is not accepted by Nova.

These source definitions establish the selected build class. They do not replace inspection of the produced archive.

## Binary review evidence and limitation

The candidate archive checksum and five extracted DLL checksums are fixed by the runtime manifest and tests. The original 0.0.9w review did not retain verbatim transcripts of:

```text
ffmpeg.exe -buildconf
ffmpeg.exe -L
```

This correction records that omission explicitly rather than reconstructing or inventing command output. The fixed candidate remains the only Nova-reviewed identity for the external-only, no-bundling route, but a future candidate update must not be approved until the exact archive has been independently acquired, checksum-matched, and the complete command transcripts are committed with the review.

For every future candidate review, retain:

1. archive checksum comparison against the upstream release checksum;
2. complete `ffmpeg.exe -buildconf` output;
3. complete `ffmpeg.exe -L` output;
4. complete extracted top-level file inventory and SHA-256 values;
5. PE dependency inventory for each accepted DLL;
6. dependency-to-license inventory;
7. confirmation that `--enable-gpl` and `--enable-nonfree` are absent;
8. confirmation that shared libraries are enabled and static FFmpeg libraries are disabled;
9. exact upstream source and build-repository commits;
10. reviewer, date, and review result.

If any evidence is missing or inconsistent, the candidate remains `Pending` and must not be changed to `Approved`.

## Scope of approval

Approval applies only to the exact built-in identity and only to Nova's external, explicitly located, dynamically loaded, non-bundled route. It does not approve:

- a host-supplied manifest;
- a modified or rebuilt runtime;
- another daily build with the same filename;
- a GPL or nonfree build;
- redistribution or rehosting by Nova;
- a third-party combined installer or runtime pack.

A host-supplied manifest records `HostDeclared`, `HostAttested`, and `Unreviewed`; the host bears responsibility for the exact build it authorizes.
