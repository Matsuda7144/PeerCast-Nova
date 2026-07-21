# PCN-0141: External LGPL Runtime Compliance and User Replacement Boundary

## Milestone

0.0.9w — amended compliance review

## Validation target

Validate an external-only FFmpeg route that preserves exact runtime integrity and user replacement without conflating a host declaration with PeerCast Nova review approval.

## Corrected result

### Two origins, two different authorities

`PlaybackFfmpegExternalRuntimeManifest` represents `BuiltInCandidate` or `HostSupplied`, but only the built-in identity is reviewed by Nova.

| Origin | Declaration authority | Authorization | Nova review | Backend license review |
| --- | --- | --- | --- | --- |
| BuiltInCandidate | `ProjectReviewed` | `BuiltInApproved` | `Approved` | `Approved` |
| HostSupplied | `HostDeclared` | `HostAttested` | `Unreviewed` | `Pending` |

The former implementation marked both origins `Approved`; this amendment removes that semantic error.

### Exact reviewed candidate

- Release tag: `autobuild-2026-07-15-14-01`
- BtbN commit: `1f74efed63f467dbf0d1e5dd8548bf2188f4ad21`
- FFmpeg commit: `94138f6973dd1ac6208ace92148ac0d172455d65`
- Asset: `ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip`
- Archive SHA-256: `F711E0478C03FD48624C59A0487E0C5D904779A3C67280A0A7B49B0002F12B43`

Approval applies only to this complete identity and the external, non-bundled route.

### Manifest contract

Both origins accept exactly the required declared string `LGPL-3.0-or-later` and five records:

| Role | Filename | ABI major |
| --- | --- | ---: |
| AvCodec | `avcodec-62.dll` | 62 |
| AvFormat | `avformat-62.dll` | 62 |
| AvUtil | `avutil-60.dll` | 60 |
| SwResample | `swresample-6.dll` | 6 |
| SwScale | `swscale-9.dll` | 9 |

Each record carries positive length and SHA-256. Creation fails closed for invalid identity, declared license class, file count, role, filename, ABI major, length, digest, or inventory. The manifest carries no path, URL, command, script, archive, downloader, installer, updater, or fallback.

`ApprovedLicenseIdentifier` was renamed to `RequiredDeclaredLicenseIdentifier` so a host's string is not represented as a Nova approval.

### Explicit host-attestation policy

Host activation requires:

```csharp
new PlaybackDecoderBackendPolicy
{
    AllowThirdParty = true,
    AllowNativeCode = true,
    AllowBundled = false,
    AllowHostAttestedRuntime = true,
}
```

Without the dedicated switch, `PlaybackFfmpegExternalRuntimeGenerationFactory` returns `PolicyRejected` before observing the runtime directory or entering native code.

Generic `PlaybackDecoderBackendPolicy.Evaluate` still rejects the host's pending provenance. `EvaluateHostAttestedRuntime` is the only explicit host-specific policy route.

### Integrity and production composition

Both origins retain:

1. one explicit runtime directory;
2. retained directory and file handles;
3. exact file count, name, size, and SHA-256 verification;
4. PE dependency inspection;
5. ABI-major validation;
6. required-symbol validation;
7. existing native-session and generation ownership.

A modified byte produces `DigestMismatch`; host attestation never disables hashing.

`PlaybackFfmpegExternalRuntimeComposition.Create` remains the application-root entry. It adds no discovery, network access, acquisition, fallback, generic registration, resident pump, second queue, or Windows resource ownership.

### Legal material correction

`THIRD-PARTY-LICENSES/FFmpeg` now contains:

- `NOTICE.md`;
- `RUNTIME-REPLACEMENT.md`;
- `PROVENANCE.md`;
- `NOVA-DISTRIBUTION-GUARD.md`;
- plain `COPYING.LGPLv3` and `COPYING.GPLv3`;
- deterministic `COPYING.GPLv3.gz` duplicate;
- `COPYING.GPLv3.README`;
- `SHA256SUMS`.

The plain GPLv3 text is 35,149 bytes with SHA-256 `3972DC9744F6499F0F9B2DBF76696F2AE7AD8AF9B23DDE66D6AF86C9DFB36986`. The gzip duplicate must decode to exactly the same bytes.

The notice and replacement guide:

- distinguish legal obligations from Nova's stricter no-bundling policy;
- state that host manifests are declarations/attestations and remain unreviewed;
- preserve reverse engineering for debugging LGPL modifications;
- include FFmpeg trademark attribution;
- prevent implication of affiliation or endorsement.

### Provenance evidence correction

`PROVENANCE.md` records the exact release, complete commits, archive, checksum, and upstream variant definitions.

The original 0.0.9w review did not retain verbatim `ffmpeg.exe -buildconf` and `ffmpeg.exe -L` transcripts. The correction records the omission instead of fabricating output. A future built-in candidate remains `Pending` until those transcripts, archive checksum comparison, extracted inventory, PE dependencies, dependency-license inventory, and reviewer record are committed.

### Release-terms guard

`NOVA-DISTRIBUTION-GUARD.md` requires review of release, installer, EULA, About, and translated terms. Any Nova-controlled prohibition that restricts reverse engineering performed to debug LGPL-library modifications blocks publication of the FFmpeg-enabled distribution.

## Test coverage

`PlaybackFfmpegExternalRuntimeManifestTests` verifies:

1. built-in reviewed/approved state;
2. host declared/attested/unreviewed/pending state;
3. explicit host-attestation policy before runtime access;
4. generic-policy rejection of host pending provenance;
5. replacement hash verification and mutation rejection;
6. fail-closed manifest validation;
7. absence of path and acquisition instructions.

`PlaybackFfmpegLegalMaterialTests` verifies:

1. readable GPLv3 and LGPLv3 fixed hashes;
2. gzip/plain GPL byte equality;
3. review/attestation distinction in notice and replacement text;
4. exact release and commit provenance;
5. explicit missing-transcript future gate;
6. trademark and reverse-engineering safeguards;
7. complete CLI build/publish legal-directory copy.

## CI record

The amendment must pass the routine CircleCI gate:

- restore;
- Release build with analyzers;
- full solution test suite;
- format verification.

The final result must not claim completion until this branch passes that gate.

## Deferred

This milestone still does not:

- bundle, mirror, rehost, download, extract, install, update, or redistribute FFmpeg;
- parse an untrusted manifest source;
- discover through `PATH`, registry, or conventional locations;
- register production FFmpeg in the generic backend factory;
- own or activate Windows application/device resources;
- perform controlled real-media output validation;
- claim audible or visible playback;
- approve a host runtime or Nova-hosted runtime pack.

## Result

The corrected boundary approves only one exact built-in external identity. Host replacements are explicitly host-declared, host-attested, and unreviewed; they require a dedicated application policy and preserve all integrity gates. Plain license texts, exact provenance, trademark language, and release-terms safeguards are included, while the missing historical binary transcripts are transparently recorded as a hard future approval gate.

## Next milestone

0.0.9x — Approved External Runtime and Windows Lifecycle Activation Review.
