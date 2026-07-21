# PCN-0141: External LGPL Runtime Compliance and User Replacement Boundary

## Milestone

0.0.9w

## Validation target

Implement the compliance conditions selected by ADR-0132 before changing the exact pinned FFmpeg candidate to `LicenseReview.Approved`: immutable built-in and host manifests, checksum-preserving user replacement, legal material in distributable output, and one application-composition-root production entry that reuses every existing runtime and ownership gate.

## Implemented boundary

### Manifest contract

`PlaybackFfmpegExternalRuntimeManifest` represents either:

- `BuiltInCandidate`; or
- `HostSupplied`.

It accepts only `LGPL-3.0-or-later` and exactly five records:

| Role | Required filename | ABI major |
| --- | --- | ---: |
| AvCodec | `avcodec-62.dll` | 62 |
| AvFormat | `avformat-62.dll` | 62 |
| AvUtil | `avutil-60.dll` | 60 |
| SwResample | `swresample-6.dll` | 6 |
| SwScale | `swscale-9.dll` | 9 |

Each record contains a positive length and one 64-character hexadecimal SHA-256 digest. The manifest materializes at most six enumeration elements, requires exactly five, orders accepted records by role, normalizes digests, and exposes an immutable list.

The manifest carries no path, directory, URL, command, script, archive, download, extraction, installation, update, or fallback instruction.

### Fail-closed results

Manifest creation returns fixed results for:

- accepted;
- invalid build ID;
- invalid version;
- rejected license;
- invalid file count;
- undefined, duplicate, or missing role;
- rejected filename;
- rejected ABI major;
- rejected file length;
- rejected digest;
- invalid inventory.

GPL, nonfree, LGPL-2.1, blank, renamed, case-variant, traversal, malformed, duplicate, and incomplete declarations do not produce a manifest.

### Approved candidate and policy

The built-in manifest is constructed from `PlaybackFfmpegBackendCandidate` and `PlaybackFfmpegBackendRuntimeInventory.PinnedCandidate`.

Its provenance is:

- `ThirdParty`;
- `OptionalExternal`;
- `Approved`;
- native code present.

The normal policy still rejects it unless the application explicitly sets:

- `AllowThirdParty = true`;
- `AllowNativeCode = true`;
- `AllowBundled = false`.

The generic backend factory is unchanged and does not register production FFmpeg.

### User replacement and runtime verification

A host creates a replacement with `PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied`.

The replacement's declared lengths and hashes are transformed into the existing `PlaybackDecoderBackendRuntimeInventory`. Both built-in and host manifests pass through:

1. one explicit runtime directory;
2. directory and file handle retention;
3. exact file count, name, size, and SHA-256 verification;
4. PE dependency inspection;
5. ABI-major validation;
6. required-symbol validation;
7. the existing production native-session and generation ownership path.

A modified byte after manifest creation produces `DigestMismatch`. No replacement path turns hashing off.

### Production composition entry

`PlaybackFfmpegExternalRuntimeGenerationFactory` wraps the existing `PlaybackProductionDecoderGenerationFactory` with manifest-specific provenance and inventory operations.

`PlaybackFfmpegExternalRuntimeComposition.Create` is the application-root entry. It supplies the selected policy and manifest to the existing finite production PCP／FLV composition while borrowing the application-owned Windows output boundary.

It does not add:

- implicit runtime discovery;
- network access;
- download, extraction, installation, or update behavior;
- fallback selection;
- generic backend registration;
- Windows adapter, COM, window, device, or stream ownership;
- a resident pump, hidden retry, or second queue.

### Legal material

`THIRD-PARTY-LICENSES/FFmpeg` now contains:

- `NOTICE.md`;
- `RUNTIME-REPLACEMENT.md`;
- `COPYING.LGPLv3`;
- `COPYING.GPLv3.gz`;
- `COPYING.GPLv3.README`;
- `SHA256SUMS`.

The decoded license-text hashes are fixed in tests:

| Text | Decoded bytes | SHA-256 |
| --- | ---: | --- |
| GPLv3 | 35149 | `3972DC9744F6499F0F9B2DBF76696F2AE7AD8AF9B23DDE66D6AF86C9DFB36986` |
| LGPLv3 | 7652 | `E3A994D82E644B03A792A930F574002658412F62407F5FEE083F2555C5F23118` |

`Nova.Cli.csproj` copies the entire directory with `CopyToOutputDirectory="PreserveNewest"` and `CopyToPublishDirectory="PreserveNewest"`.

## Test coverage

`PlaybackFfmpegExternalRuntimeManifestTests` verifies:

1. built-in manifest identity and approved provenance;
2. modified host runtime authorization with hashing retained;
3. rejection of nonapproved license classes;
4. rejection of missing roles;
5. rejection of duplicate roles;
6. rejection of unrecognizable or path-bearing filenames;
7. rejection of wrong ABI majors;
8. rejection of nonpositive lengths;
9. rejection of invalid digests;
10. rejection of path／command／URL-like identity syntax;
11. explicit third-party and native policy requirements;
12. absence of path or acquisition instructions from the public contract.

`PlaybackFfmpegLegalMaterialTests` verifies:

1. fixed verbatim GNU license hashes;
2. prominent notice and complete replacement instructions;
3. legal-directory copy settings for CLI build and publish.

Candidate and integration-probe tests verify the `Pending → Approved` transition. A nonexistent explicit directory now reaches the runtime gate and returns `RuntimeRejected`; it no longer stops at the obsolete `LicenseReviewPending` result.

## CI record

The final implementation head passed the routine CircleCI gate:

- restore;
- Release build with analyzers;
- full solution test suite;
- format verification.

Temporary analyzer, formatting, and test-isolation configurations and all generated diagnostic logs were removed before final validation. Routine `.circleci/config.yml` remains the merge gate.

## Files added or updated

- `THIRD-PARTY-LICENSES/FFmpeg/COPYING.GPLv3.README`
- `THIRD-PARTY-LICENSES/FFmpeg/COPYING.GPLv3.gz`
- `THIRD-PARTY-LICENSES/FFmpeg/COPYING.LGPLv3`
- `THIRD-PARTY-LICENSES/FFmpeg/NOTICE.md`
- `THIRD-PARTY-LICENSES/FFmpeg/RUNTIME-REPLACEMENT.md`
- `THIRD-PARTY-LICENSES/FFmpeg/SHA256SUMS`
- `src/Nova.Cli/Nova.Cli.csproj`
- `src/Nova.Playback/PlaybackFfmpegBackendCandidate.cs`
- `src/Nova.Playback/PlaybackFfmpegExternalRuntimeManifest.cs`
- `src/Nova.Playback/PlaybackFfmpegExternalRuntimeProductionComposition.cs`
- `src/Nova.Playback/PlaybackProductionDecoderGenerationFactory.cs`
- candidate, manifest, legal-material, CLI-probe, and playback-probe tests
- ADR-0133, PCN-0141, and project milestone ledgers

## Deferred

This milestone does not:

- bundle, mirror, rehost, download, extract, install, update, or redistribute FFmpeg;
- parse a manifest from an untrusted file or network source;
- discover a runtime through `PATH`, registry, or conventional directories;
- register production FFmpeg in the generic backend factory;
- own or activate a Windows window, adapter, COM apartment, audio device, or rendering resource;
- perform controlled real-media decode or output validation;
- claim audible or visible playback;
- approve a Nova-hosted runtime pack.

## Result

The external-only LGPL compliance and user-replacement gate is complete. The pinned candidate is now `LicenseReview.Approved`, both built-in and host-authorized runtimes preserve exact integrity verification, and distributable CLI output carries the required legal material.

## Next milestone

0.0.9x — Approved External Runtime and Windows Lifecycle Activation Review.