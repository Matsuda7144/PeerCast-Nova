# ADR-0132: FFmpeg Candidate License, Backend Registration, and Distribution Review

## Status

Accepted for milestone 0.0.9v.

## Context

PeerCast Nova is Apache License 2.0 software. The production playback path now reaches a policy-gated, source-bound FFmpeg generation, but the pinned candidate remains `PlaybackDecoderBackendLicenseReview.Pending` and therefore fails closed before runtime observation or native activation.

The reviewed candidate is:

- upstream build project: BtbN/FFmpeg-Builds;
- build ID: `btbn.ffmpeg.20260715.win64.lgpl-shared`;
- FFmpeg version: `n8.1.2-22-g94138f6973-20260715`;
- archive name: `ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip`;
- archive SHA-256: `F711E0478C03FD48624C59A0487E0C5D904779A3C67280A0A7B49B0002F12B43`;
- declared license: `LGPL-3.0-or-later`;
- distribution form: optional external, host supplied;
- linkage form: shared DLLs loaded from one explicit installation directory.

This review must resolve four separate questions:

1. whether the selected build class is legally suitable for an Apache-2.0 application;
2. whether Nova may bundle or redistribute the selected third-party binary;
3. how users can exercise LGPL replacement and modification rights without weakening the explicit-directory and integrity boundaries;
4. where the production FFmpeg path is registered without bypassing the generation ownership model.

This record is project policy and engineering review, not legal advice.

## Evidence reviewed

### FFmpeg licensing

The FFmpeg project states that most FFmpeg code is LGPL-2.1-or-later, that `--enable-gpl` changes the complete FFmpeg build to GPL, and that `--enable-nonfree` can produce an unredistributable binary. Its compliance checklist recommends a build without either option and dynamic linking through DLLs on Windows.

The current FFmpeg license file also states that `--enable-version3` upgrades the applicable LGPL version to LGPLv3 where required by selected components.

### BtbN build variant

BtbN documents separate `gpl`, `lgpl`, and `nonfree` variants. Its `lgpl` variant excludes GPL-only dependencies, while `lgpl-shared` adds `--enable-shared --disable-static`.

The reviewed default LGPL variant sets:

- `--enable-version3`;
- `LICENSE_FILE="COPYING.LGPLv3"`;
- no `--enable-gpl`;
- no `--enable-nonfree`.

The selected build class is therefore an LGPLv3 shared-library build rather than a GPL or nonfree build.

### LGPLv3 replacement requirement

LGPLv3 section 4 permits an application under terms of its choice when the combined work provides notices and license texts and uses either relinkable application material or a suitable shared-library mechanism. The shared-library mechanism must use a library already present on the user's system and operate with an interface-compatible modified replacement.

The current Nova integrity inventory accepts only one built-in fixed set of DLL hashes. That is a valid supply-chain safety control, but by itself it is not a sufficient user-replacement mechanism for an activated LGPLv3 runtime.

## Decision

### License classification

The exact `win64-lgpl-shared` candidate class is **legally acceptable in principle** for external dynamic use by the Apache-2.0 Nova application because:

- it is the LGPL rather than GPL or nonfree variant;
- it uses shared DLLs rather than static linking;
- Nova contains no copied FFmpeg source, generated binding source, or FFmpeg binary;
- Nova's Apache-2.0 terms do not prohibit library modification or reverse engineering for debugging those modifications.

This architecture decision does not immediately mutate the runtime candidate to `Approved`. Runtime approval remains blocked until the notice, license-copy, and user-replacement requirements below are implemented and tested.

### Distribution policy

Nova release artifacts must not:

- bundle FFmpeg executables, DLLs, headers, import libraries, or archives;
- mirror or rehost BtbN binary assets;
- automatically download, update, extract, or install FFmpeg;
- search `PATH`, the registry, system directories, or conventional installation locations;
- silently substitute another FFmpeg build, GPL variant, nonfree variant, or fallback runtime.

The user or application host supplies one explicit runtime directory. Nova may publish the reviewed build identity, checksums, build instructions, upstream links, and compatibility requirements, but it does not convey the FFmpeg binary.

Because Nova does not convey the FFmpeg DLLs, Nova does not assume the binary distributor's corresponding-source obligations. Any person or project that later redistributes an FFmpeg binary with Nova becomes responsible for the exact corresponding source, build configuration, patches, license inventory, notices, and continuing source availability for that binary and its linked components.

Bundling or a separate Nova-hosted runtime pack requires a new ADR and a complete binary／source compliance archive. It is not approved by this decision.

### Required legal material

Before runtime approval changes from `Pending`, every Nova binary distribution that can use FFmpeg must include:

- a prominent third-party notice that FFmpeg is used under LGPLv3;
- verbatim copies of GNU GPLv3 and GNU LGPLv3;
- the exact candidate identity and upstream build provenance;
- an explanation that FFmpeg is not part of the Apache-2.0 Nova source license;
- the user-facing runtime replacement procedure;
- no restriction on reverse engineering performed to debug modifications to the LGPL library;
- unmodified, recognizable FFmpeg DLL names.

A future GUI or about screen must expose the FFmpeg notice and a route to the included license texts whenever the FFmpeg backend is available.

### User-controlled replacement without implicit trust

Add one explicit host-owned runtime manifest path in the next implementation milestone.

The host manifest must provide:

- expected FFmpeg major identity and build label;
- the exact required DLL filenames;
- one SHA-256 per DLL;
- declared license class restricted to `LGPL-3.0-or-later`;
- explicit confirmation that the manifest is host supplied and not a Nova redistribution record.

Nova will continue to:

- require one explicit directory;
- verify every declared checksum before loading;
- require the exact reviewed ABI major and symbol set;
- retain directory and DLL handles through the existing lifetime boundaries;
- reject missing, extra-role, GPL, nonfree, undefined, or inconsistent manifests;
- perform no network, registry, or `PATH` discovery.

This preserves fail-closed integrity while allowing a user to authorize an interface-compatible modified LGPL build. The built-in BtbN manifest remains a convenient reviewed candidate, not the only technically possible replacement.

### Runtime approval transition

`PlaybackFfmpegBackendCandidate.Provenance.LicenseReview` remains `Pending` in 0.0.9v.

The next implementation may change it to `Approved` only after all of the following are present:

1. included GPLv3 and LGPLv3 texts;
2. included third-party notice and replacement instructions;
3. explicit host-manifest validation;
4. tests proving the built-in candidate and a host-approved compatible replacement are both finite and fail closed;
5. tests proving bundled／GPL／nonfree／implicit-discovery paths remain rejected.

### Backend registration

Do not register production FFmpeg through the generic `PlaybackDecoderBackendFactory`.

That factory selects an `IPlaybackDecoderBackend` from a recognized container and cannot carry the production requirements for:

- explicit runtime directory and runtime manifest;
- exact parser-created source session;
- borrowed Windows output boundary;
- decoder and discontinuity generations;
- asynchronous complete-product construction;
- replacement and ordered stop ownership.

Production registration belongs at the application composition root. The application explicitly constructs the production generation factory with:

- `AllowThirdParty = true`;
- `AllowNativeCode = true`;
- `AllowBundled = false`;
- the approved candidate or host manifest;
- one explicit runtime directory;
- the application-owned Windows output environment.

The generic backend factory remains available for its existing descriptor-based diagnostic or non-production uses and must not become a shortcut around the production policy gate.

### Daily-build retention

The selected BtbN daily asset is an exact technical candidate, not a permanent Nova distribution location. BtbN's documented retention policy removes ordinary daily releases after a limited period.

Nova therefore must not treat continued availability of that asset as part of its runtime contract. A user may retain the verified archive, reproduce an approved compatible build, or provide another host-approved compatible manifest. Nova does not promise that the historical third-party download remains online.

### Patent boundary

This review addresses copyright-license and distribution architecture only. It does not grant or assert patent rights for H.264, AAC, MPEG, or any other codec and does not make a jurisdiction-specific patent clearance decision.

## Rejected alternatives

- Bundling the reviewed BtbN archive directly inside Nova releases.
- Rehosting the third-party archive while linking only to upstream source.
- Treating `lgpl-shared` naming alone as sufficient evidence without build-variant review.
- Enabling GPL or nonfree builds under the Apache-2.0 default distribution.
- Automatically downloading an FFmpeg runtime on first launch.
- Using `PATH`, registry, or conventional-directory discovery.
- Keeping a fixed built-in hash as the only possible activated LGPL replacement route.
- Disabling checksum verification entirely to permit replacement.
- Registering the production runtime through `PlaybackDecoderBackendFactory`.
- Marking the candidate `Approved` before notices, license copies, and replacement support exist.

## Consequences

- The external shared-library architecture is accepted as the permanent default distribution model.
- Nova will remain Apache-2.0 and will not distribute FFmpeg binaries.
- The reviewed BtbN LGPLv3 shared candidate is conditionally approvable, but runtime state remains `Pending` during this review milestone.
- User replacement will be explicit and checksum-verified rather than implicit or unverified.
- Production FFmpeg registration will occur only at the application composition root.
- Public activation still fails closed until the next compliance implementation completes.
- No runtime source or tests are changed by 0.0.9v.

## Next

0.0.9w — External LGPL Runtime Compliance and User Replacement Boundary.
