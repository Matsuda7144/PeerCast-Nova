# ADR-0133: External LGPL Runtime Compliance and User Replacement Boundary

## Status

Accepted for milestone 0.0.9w; amended by the 0.0.9w host-attestation compliance correction.

## Context

ADR-0132 accepted an external LGPLv3 shared-library architecture for one exact reviewed FFmpeg candidate, but kept approval pending until legal material and a checksum-preserving user replacement route existed.

The initial 0.0.9w implementation incorrectly used `LicenseReview.Approved` for both the reviewed built-in identity and arbitrary host-supplied manifests. A host declaration cannot be converted into a Nova legal or technical review. The original legal package also included GPLv3 only as a compressed file and did not retain the complete binary command transcripts expected for future candidate reviews.

This record is an engineering compliance decision, not legal advice.

## Decision

### Immutable runtime manifest

`PlaybackFfmpegExternalRuntimeManifest` remains the only manifest type for the production external route. It has one origin:

- `BuiltInCandidate`; or
- `HostSupplied`.

Both origins carry only:

- bounded build ID and version labels;
- the required declared license identifier `LGPL-3.0-or-later`;
- exactly five required library records;
- each role, recognizable filename, ABI major, positive length, and SHA-256 digest.

The contract contains no runtime directory, path, URL, archive location, command, script, downloader, installer, updater, or fallback location.

### Exact accepted inventory

| Role | Filename | ABI major |
| --- | --- | ---: |
| AvCodec | `avcodec-62.dll` | 62 |
| AvFormat | `avformat-62.dll` | 62 |
| AvUtil | `avutil-60.dll` | 60 |
| SwResample | `swresample-6.dll` | 6 |
| SwScale | `swscale-9.dll` | 9 |

Creation rejects invalid or unbounded identity, any differently declared license class, invalid file count, undefined/duplicate/missing roles, renamed or path-bearing filenames, wrong ABI majors, nonpositive lengths, malformed digests, and invalid runtime inventory. Digests are normalized and the accepted file list is immutable.

### Reviewed built-in candidate

Only `BuiltInCandidate` is reviewed by Nova. Its exact identity is:

- BtbN release tag `autobuild-2026-07-15-14-01`;
- BtbN commit `1f74efed63f467dbf0d1e5dd8548bf2188f4ad21`;
- FFmpeg commit `94138f6973dd1ac6208ace92148ac0d172455d65`;
- archive `ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip`;
- archive SHA-256 `F711E0478C03FD48624C59A0487E0C5D904779A3C67280A0A7B49B0002F12B43`.

Its states are:

- `ProjectReviewed`;
- `BuiltInApproved`;
- `Approved`;
- third-party, optional external, native code, not bundled.

Application policy must still explicitly allow third-party and native code and keep bundling disabled.

### Host-controlled replacement

`CreateHostSupplied` records a host's declaration and attestation for one exact compatible local build. It does not record a Nova approval.

Its states are:

- `HostDeclared`;
- `HostAttested`;
- `Unreviewed`;
- backend license review `Pending`.

The host is responsible for confirming actual configuration, dependency licenses, and suitability. Nova continues to enforce the required declared license string, fixed filenames, ABI majors, exact file lengths and SHA-256 values, PE dependencies, and required symbols.

Host activation requires the separate application policy switch:

```csharp
AllowHostAttestedRuntime = true
```

The route returns `PolicyRejected` before runtime-directory inspection or native loading when that switch is absent. Generic `PlaybackDecoderBackendPolicy.Evaluate` continues to reject pending provenance; ordinary policy cannot silently enable an unreviewed backend.

### Singular production composition

`PlaybackFfmpegExternalRuntimeGenerationFactory` and `PlaybackFfmpegExternalRuntimeComposition` remain the application-composition-root entry. The host-specific preflight evaluates explicit attestation before adapting into the existing reviewed FFmpeg ABI/backend class and runtime inventory gates.

Runtime location still uses one explicit directory, retained directory and DLL handles, exact inventory verification, PE dependency validation, ABI-major validation, required symbols, and source-bound generation ownership.

The composition adds no generic backend registration, discovery, network access, download, extraction, installation, update, fallback, second queue, resident loop, or Windows resource ownership.

### Legal material in distributable output

`THIRD-PARTY-LICENSES/FFmpeg` contains:

- `NOTICE.md`;
- `RUNTIME-REPLACEMENT.md`;
- `PROVENANCE.md`;
- `NOVA-DISTRIBUTION-GUARD.md`;
- readable `COPYING.LGPLv3` and `COPYING.GPLv3`;
- deterministic `COPYING.GPLv3.gz` duplicate and README;
- `SHA256SUMS`.

The notice separates license obligations from Nova's stricter no-bundling policy, identifies host manifests as unreviewed attestations, includes trademark attribution, and preserves reverse engineering for debugging LGPL modifications.

`Nova.Cli` copies the complete legal directory to build and publish output.

### Provenance limitation and future gate

`PROVENANCE.md` records the exact release tag, complete build-repository and FFmpeg commits, asset, checksum, and exact upstream variant definitions.

The original review did not retain verbatim `ffmpeg.exe -buildconf` and `ffmpeg.exe -L` transcripts. This amendment records the gap; it does not fabricate transcripts. Any future built-in candidate remains `Pending` until complete command transcripts, checksum comparison, extracted inventory, PE dependencies, dependency-license inventory, and reviewer record are committed.

### Verification

Cross-platform tests prove:

- only the built-in candidate is project-reviewed and approved;
- a host manifest is host-declared, host-attested, unreviewed, and pending;
- explicit host-attestation policy is required before runtime access;
- generic policy still rejects host pending provenance;
- replacement hashes remain enforced;
- invalid licenses, identities, roles, filenames, ABI majors, lengths, and digests fail closed;
- the public manifest surface has no path or acquisition instruction;
- readable GNU license texts have fixed hashes and the gzip duplicate matches the plain GPL text;
- notice, replacement, provenance, trademark, reverse-engineering, and release-guard statements are present;
- CLI output copies the complete legal directory.

## Rejected alternatives

- Treating a host declaration as `LicenseReview.Approved`.
- Reusing the `Approved` name for a required host-declared license string.
- Allowing host pending provenance through ordinary generic policy.
- Disabling checksums for modified builds.
- Accepting free-form filenames, paths, URLs, commands, or acquisition instructions.
- Accepting GPL, nonfree, unknown, or differently declared license classes.
- Discovering FFmpeg through `PATH`, registry, system directories, or common locations.
- Registering production FFmpeg in the generic backend factory.
- Bundling or rehosting FFmpeg binaries.
- Claiming binary command output that was not retained.

## Consequences

- The exact built-in candidate remains approved only for the external, non-bundled route.
- Host-selected builds remain explicitly unreviewed and require application attestation.
- Integrity and native safety gates remain unchanged.
- Plain license texts and release-terms safeguards are present in distributable output.
- Missing binary transcript evidence is visible and becomes a hard gate for future candidate changes.
- Windows lifecycle activation and controlled real-media playback remain separate work.

## Next

0.0.9x — Approved External Runtime and Windows Lifecycle Activation Review.
