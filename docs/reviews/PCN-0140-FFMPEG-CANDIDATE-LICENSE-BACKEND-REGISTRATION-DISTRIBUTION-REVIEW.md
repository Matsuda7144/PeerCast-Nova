# PCN-0140: FFmpeg Candidate License, Backend Registration, and Distribution Review

## Milestone

0.0.9v

## Validation target

Resolve the license class, binary distribution policy, user-replacement contract, and production registration location for the exact pinned FFmpeg candidate without weakening the current fail-closed runtime or claiming activation before compliance implementation exists.

## Candidate reviewed

| Field | Value |
| --- | --- |
| Build project | BtbN/FFmpeg-Builds |
| Build ID | `btbn.ffmpeg.20260715.win64.lgpl-shared` |
| FFmpeg version | `n8.1.2-22-g94138f6973-20260715` |
| Archive | `ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip` |
| Archive SHA-256 | `F711E0478C03FD48624C59A0487E0C5D904779A3C67280A0A7B49B0002F12B43` |
| Manifest license | `LGPL-3.0-or-later` |
| Provenance distribution | `OptionalExternal` |
| Current runtime review | `Pending` |

## Primary evidence

### FFmpeg

The official FFmpeg legal page states that:

- FFmpeg is LGPL-2.1-or-later by default;
- `--enable-gpl` changes the complete FFmpeg license to GPL;
- `--enable-nonfree` can make the resulting binary unredistributable;
- the recommended Windows LGPL route uses dynamic DLL linking;
- binary distributors must pair exact binaries with exact corresponding source and build information;
- notices, license access, replacement freedom, recognizable DLL names, and absence of reverse-engineering restrictions are part of the recommended compliance route.

The current FFmpeg license file states that `--enable-version3` selects the version 3 license path for configurations that need it.

### BtbN

The BtbN repository separates `gpl`, `lgpl`, and `nonfree` dependency variants. The reviewed scripts show:

```text
defaults-lgpl.sh:
  FF_CONFIGURE="--enable-version3 --disable-debug"
  LICENSE_FILE="COPYING.LGPLv3"

defaults-lgpl-shared.sh:
  --enable-shared --disable-static
```

The `win64-lgpl-shared` variant composes the Windows shared installation with those LGPL shared defaults.

### GNU LGPLv3

LGPLv3 section 4 permits an application under terms of its choice when it provides required notice／license material and either relinkable application material or a suitable shared-library mechanism. The suitable shared mechanism must operate with an interface-compatible modified version supplied on the user's system.

The GNU license FAQ distinguishes host-present dynamic linking from conveying the LGPL binary: when the application distributor does not convey the library, the application distributor does not also convey that library's source; when it does convey the library, source conveyance obligations apply.

## Findings

### License class

The reviewed candidate is an LGPLv3 shared-library build class. It is not the BtbN GPL variant and is not the nonfree variant.

The Apache-2.0 Nova source can remain under Apache License 2.0 while using this external LGPL shared-library architecture, provided the activated distribution satisfies LGPL notices, license-copy, replacement, and debugging-modification conditions.

### Binary distribution

The existing host-provided model is selected permanently for the default distribution:

`Nova release → no FFmpeg binary`

`user／host → explicit runtime directory → verified shared DLLs → Nova production generation`

Nova does not become the FFmpeg binary distributor and does not inherit a binary packager's corresponding-source duties.

A separate party that combines Nova and FFmpeg binaries is responsible for that combined distribution's exact source and notice compliance.

### Replacement gap

The current built-in fixed DLL hashes provide strong integrity, but they are the only accepted runtime identity. An activated LGPLv3 path needs a usable interface-compatible replacement route.

The selected solution is not to remove hashing. Add an explicit host-owned manifest containing the replacement DLL names, roles, ABI major, license class, and hashes. The user chooses the trusted modified library; Nova verifies exactly what the user chose.

### Registration location

`PlaybackDecoderBackendFactory` is not the production registration point. It selects a backend from a container descriptor and lacks the runtime directory, source session, generation IDs, async complete-product creation, borrowed Windows output, replacement, and ordered stop contracts.

Production registration belongs at the application composition root and must explicitly construct the policy-gated production generation factory and finite production PCP／FLV composition.

## Decision assertions

1. The exact BtbN `lgpl-shared` candidate class is acceptable in principle for external dynamic use.
2. Nova does not bundle, mirror, rehost, download, extract, update, or install FFmpeg.
3. Nova continues to accept only one explicit runtime directory and performs no implicit discovery.
4. GPL and nonfree FFmpeg variants remain rejected.
5. The built-in candidate remains `LicenseReview.Pending` until legal materials and user replacement are implemented.
6. Activated distributions must include verbatim GPLv3 and LGPLv3 texts and a prominent FFmpeg notice.
7. A host-owned replacement manifest is required before approval.
8. Checksum and ABI verification remain mandatory for both built-in and replacement manifests.
9. Production registration occurs only at the application composition root.
10. The generic backend factory remains unchanged.
11. BtbN daily release retention is not a Nova availability guarantee.
12. Binary bundling requires a new ADR and a full corresponding-source compliance archive.
13. Patent clearance is outside this copyright-license review.

## Required next implementation

0.0.9w must add a finite external-runtime compliance boundary that:

- carries bounded built-in and host manifest records;
- accepts `LGPL-3.0-or-later` only;
- validates exact DLL roles, filenames, hashes, and FFmpeg ABI major;
- rejects traversal, duplicate roles, missing roles, GPL, nonfree, and undefined values;
- composes with the current explicit-directory locator and ABI symbol gate;
- includes the GPLv3 and LGPLv3 license texts and third-party notice in distributable artifacts;
- exposes a user-readable replacement procedure;
- proves a host-approved compatible replacement can pass without disabling integrity verification;
- proves bundled and implicit-discovery routes remain impossible;
- only then changes the built-in candidate review to `Approved`.

## Review-only scope

This milestone does not:

- change `PlaybackFfmpegBackendCandidate`;
- change `PlaybackDecoderBackendLicenseReview.Pending`;
- load, activate, or execute FFmpeg;
- register FFmpeg in `PlaybackDecoderBackendFactory`;
- add a runtime manifest parser;
- include GPLv3／LGPLv3 text files in release output yet;
- bundle or redistribute FFmpeg;
- add network download or update behavior;
- change Windows device ownership;
- claim audible or visible playback.

## Files added or updated

- `docs/adr/ADR-0132-ffmpeg-candidate-license-backend-registration-distribution-review.md`
- `docs/reviews/PCN-0140-FFMPEG-CANDIDATE-LICENSE-BACKEND-REGISTRATION-DISTRIBUTION-REVIEW.md`
- `docs/legal/FFMPEG-EXTERNAL-RUNTIME-COMPLIANCE.md`
- `docs/legal/THIRD-PARTY-AND-PROVENANCE.md`
- project navigation and milestone ledgers

No runtime source or tests are changed.

## Next milestone

0.0.9w — External LGPL Runtime Compliance and User Replacement Boundary.
