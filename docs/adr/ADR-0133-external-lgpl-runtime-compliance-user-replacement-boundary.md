# ADR-0133: External LGPL Runtime Compliance and User Replacement Boundary

## Status

Accepted for milestone 0.0.9w.

## Context

ADR-0132 accepted an external LGPLv3 shared-library architecture for the reviewed FFmpeg candidate, but deliberately kept `PlaybackDecoderBackendLicenseReview.Pending` until the distribution contained the required legal material and the runtime path allowed an interface-compatible user replacement without disabling integrity verification.

The existing runtime boundary already required one explicit directory, retained directory and DLL handles, exact file size and SHA-256 checks, PE dependency validation, reviewed ABI majors, and required symbols. The missing implementation was a bounded authorization record that could represent either the reviewed built-in candidate or a user-selected compatible modified LGPL build.

This record is an engineering compliance decision, not legal advice.

## Decision

### Immutable runtime manifest

Add `PlaybackFfmpegExternalRuntimeManifest` as the only manifest type for the approved production route.

A manifest has one fixed origin:

- `BuiltInCandidate`; or
- `HostSupplied`.

It carries only:

- a bounded build ID;
- a bounded version label;
- the fixed license identifier `LGPL-3.0-or-later`;
- exactly five required library records;
- each library role, recognizable filename, reviewed ABI major, positive file length, and SHA-256 digest.

The public manifest contract contains no runtime directory, path, URL, archive location, command, script, download instruction, installation instruction, or fallback location.

### Exact accepted inventory

The required role and ABI mapping is:

| Role | Filename | ABI major |
| --- | --- | ---: |
| AvCodec | `avcodec-62.dll` | 62 |
| AvFormat | `avformat-62.dll` | 62 |
| AvUtil | `avutil-60.dll` | 60 |
| SwResample | `swresample-6.dll` | 6 |
| SwScale | `swscale-9.dll` | 9 |

Manifest creation rejects:

- an invalid or unbounded build ID or version;
- any license identifier other than `LGPL-3.0-or-later`;
- a null, short, or oversized inventory;
- undefined, duplicate, or missing roles;
- renamed, case-variant, or path-bearing filenames;
- a wrong ABI major;
- nonpositive file lengths;
- malformed SHA-256 values;
- any inventory that cannot be converted into the existing runtime inventory contract.

Accepted digests are normalized to uppercase and the resulting file list is immutable.

### Built-in candidate approval

`PlaybackFfmpegExternalRuntimeManifest.BuiltInCandidate` is constructed from the existing pinned FFmpeg inventory and exact candidate identity. It is not a bundled binary and does not acquire or locate a runtime.

After the legal material, host replacement path, and tests described below exist, the reviewed candidate provenance changes from `LicenseReview.Pending` to `LicenseReview.Approved`.

The approved provenance remains:

- third party;
- optional external distribution;
- native code present;
- not bundled.

An application policy must still explicitly allow third-party and native code and keep bundled code disabled.

### Host-controlled replacement

`PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied` lets the user or application host authorize one interface-compatible modified LGPL build.

The host chooses the exact five files and records their lengths and SHA-256 values. Nova then verifies those exact files through the existing inventory verifier. A byte change after manifest construction produces a digest mismatch; replacement support never disables hashing.

Nova does not determine whether an arbitrary local build is legally or technically suitable. The manifest author supplies that authorization, while Nova continues to enforce the fixed LGPL license class, recognizable filenames, reviewed ABI majors, and required runtime gates.

### Singular production composition

Add `PlaybackFfmpegExternalRuntimeGenerationFactory` and `PlaybackFfmpegExternalRuntimeComposition` as the application-composition-root entry for the approved route.

The generation factory adapts the selected manifest into the existing `PlaybackProductionDecoderGenerationFactory`. Runtime location still goes through `PlaybackDecoderBackendInstalledRuntimeLocator.LocateAndVerifyAsync`, followed by the existing retained-handle, PE dependency, ABI, and required-symbol boundaries.

The static composition entry constructs the existing finite `PlaybackProductionPcpFlvCompositionBoundary` with:

- channel and channel-session identity;
- one explicit runtime directory;
- one borrowed `PlaybackWindowsOutputBufferProcessingBoundary`;
- one explicit backend policy;
- one built-in or host-supplied manifest;
- finite parser, decode, scheduler, timing, and generation options.

It adds no generic backend registration, discovery, download, extraction, installation, update, fallback, second queue, resident loop, or Windows resource ownership.

### Legal material in distributable output

Add `THIRD-PARTY-LICENSES/FFmpeg` containing:

- `NOTICE.md`;
- `RUNTIME-REPLACEMENT.md`;
- verbatim `COPYING.LGPLv3`;
- deterministic `COPYING.GPLv3.gz` plus an extraction README;
- `SHA256SUMS` for the included license texts.

The notice states that FFmpeg is external LGPL software, not part of Nova's Apache-2.0 license, that Nova does not bundle the runtime, and that reverse engineering for debugging modifications to the LGPL library is not prohibited.

The replacement instructions name the required DLLs, describe `PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied`, require an explicit directory, and preserve `AllowBundled = false`.

`Nova.Cli` copies the complete legal directory to build and publish output.

### Verification

Cross-platform tests prove:

- the built-in candidate matches the pinned inventory and is approved external provenance;
- a host manifest with modified file hashes is accepted and still hash-verified;
- GPL, nonfree, LGPL-2.1, empty, malformed, duplicate, missing, renamed, path-bearing, wrong-ABI, invalid-length, and invalid-digest manifests fail closed;
- approved external provenance still requires explicit third-party and native-code policy;
- the public manifest surface contains no path or acquisition instruction;
- included GNU license texts have fixed decoded SHA-256 values;
- notice and replacement instructions contain the required statements and API names;
- CLI build and publish copy the complete legal directory;
- previous diagnostic probes now reach the runtime gate rather than returning the obsolete pending-review result.

## Rejected alternatives

- Approving the candidate while retaining a fixed built-in hash as the only runtime identity.
- Disabling checksum verification for user-modified builds.
- Accepting free-form filenames, paths, URLs, commands, or download instructions in a manifest.
- Accepting GPL, nonfree, unknown, or LGPL-2.1 declarations.
- Discovering FFmpeg through `PATH`, registry, system directories, or conventional locations.
- Registering this route in the generic `PlaybackDecoderBackendFactory`.
- Bundling or rehosting FFmpeg binaries.
- Giving the new composition ownership of Windows activation or device resources.
- Adding an automatic pump, retry loop, fallback runtime, or second queue.

## Consequences

- The exact reviewed built-in candidate can now be represented as `LicenseReview.Approved`.
- A user can authorize a compatible modified LGPL runtime without weakening file integrity verification.
- Every activated runtime still passes the existing explicit-directory and native safety gates.
- Legal notice, license, and replacement material is present in CLI build and publish output.
- FFmpeg binaries remain outside Nova source, release, installer, and update artifacts.
- The approved runtime route exists at the application composition root, but Windows application lifecycle activation and controlled real-media playback remain separate work.

## Next

0.0.9x — Approved External Runtime and Windows Lifecycle Activation Review.