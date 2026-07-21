# Replacing the External FFmpeg Runtime

PeerCast Nova uses FFmpeg only from one explicit local directory supplied by the application host. It does not search `PATH`, the Windows registry, system directories, or conventional installation locations, and it does not download or install a runtime.

## Authorization model

`PlaybackFfmpegExternalRuntimeManifest.BuiltInCandidate` is the one identity reviewed by the PeerCast Nova project.

`PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied` creates a different authorization class:

- the license string is declared by the host;
- the exact files are attested by the host;
- the manifest remains `Unreviewed` by PeerCast Nova;
- Nova does not represent it as `LicenseReview.Approved`;
- activation requires the separate `AllowHostAttestedRuntime = true` policy switch.

The application host is responsible for confirming the actual FFmpeg configuration, all dependency licenses, and the legal and technical suitability of the exact build it authorizes.

## Required compatible library set

A replacement runtime must provide exactly these recognizable shared-library names and ABI majors:

| Role | Required file name | ABI major |
| --- | --- | ---: |
| Codec | `avcodec-62.dll` | 62 |
| Format dependency | `avformat-62.dll` | 62 |
| Utility | `avutil-60.dll` | 60 |
| Audio resampling | `swresample-6.dll` | 6 |
| Video scaling | `swscale-9.dll` | 9 |

Although the current production decoder uses H.264, AAC, and utility symbols, the complete verified five-DLL dependency set must be present. Missing files, additional top-level files, renamed DLLs, path-bearing names, wrong ABI majors, missing required symbols, unexpected PE dependencies, size mismatches, or digest mismatches fail closed.

## Required host declaration

The host manifest must declare exactly:

```text
LGPL-3.0-or-later
```

This field records a host declaration. It is not proof of the binary's configuration and is not a Nova legal review. GPL, nonfree, unknown, undefined, or differently declared license classes are rejected by this route.

## Create a host manifest

For each DLL, calculate its exact byte length and SHA-256 digest, then create the manifest:

```csharp
var creation = PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied(
    buildId: "my.ffmpeg.8.1.modified",
    version: "8.1-custom+local1",
    licenseIdentifier:
        PlaybackFfmpegExternalRuntimeManifest.RequiredDeclaredLicenseIdentifier,
    files:
    [
        new(PlaybackFfmpegExternalRuntimeLibraryRole.AvCodec,
            "avcodec-62.dll", 62, avcodecLength, avcodecSha256),
        new(PlaybackFfmpegExternalRuntimeLibraryRole.AvFormat,
            "avformat-62.dll", 62, avformatLength, avformatSha256),
        new(PlaybackFfmpegExternalRuntimeLibraryRole.AvUtil,
            "avutil-60.dll", 60, avutilLength, avutilSha256),
        new(PlaybackFfmpegExternalRuntimeLibraryRole.SwResample,
            "swresample-6.dll", 6, swresampleLength, swresampleSha256),
        new(PlaybackFfmpegExternalRuntimeLibraryRole.SwScale,
            "swscale-9.dll", 9, swscaleLength, swscaleSha256),
    ]);

if (creation.Result != PlaybackFfmpegExternalRuntimeManifestResult.Accepted ||
    creation.Manifest is null)
{
    // Do not activate playback. Report the fixed manifest result.
}
```

The manifest contains no directory, URL, downloader, command, script, or installer. It is only an immutable host attestation for exact local files.

## Activate through the application composition root

```csharp
var composition = PlaybackFfmpegExternalRuntimeComposition.Create(
    channelId,
    channelSessionId,
    runtimeDirectory,
    windowsOutputBoundary,
    new PlaybackDecoderBackendPolicy
    {
        AllowThirdParty = true,
        AllowNativeCode = true,
        AllowBundled = false,
        AllowHostAttestedRuntime = true,
    },
    creation.Manifest);
```

Without `AllowHostAttestedRuntime = true`, a host manifest returns `PolicyRejected` before Nova inspects the runtime directory or enters native loading. The built-in reviewed manifest does not require this host-attestation switch.

Every activated runtime still passes the fixed-drive and reparse-point checks, retained directory and file handles, exact inventory verification, PE dependency inspection, DLL loading restrictions, ABI-major checks, and required-symbol checks. Host attestation does not disable or bypass a runtime gate.

## Modifying or rebuilding FFmpeg

An interface-compatible modified LGPL shared build may be used by generating a new host manifest for the resulting DLLs. Do not reuse the built-in hashes after modifying or rebuilding a library.

PeerCast Nova does not impose a restriction on reverse engineering performed to debug modifications to the LGPL libraries. Nova release, installer, EULA, and translated terms must preserve that freedom.

## Legal obligations and Nova policy

PeerCast Nova's no-bundling rule is a project distribution policy. It does not replace the LGPL and is not a complete compliance guide. Anyone redistributing FFmpeg binaries must evaluate and satisfy the obligations applicable to that distribution, including corresponding source, build scripts and configuration, modifications, notices, relinking or replacement ability, dependency licenses, installation information where applicable, and source availability.

This host-installed procedure does not authorize bundling FFmpeg with PeerCast Nova. A third-party combined package is outside Nova's default release boundary and requires its own documented compliance review.
