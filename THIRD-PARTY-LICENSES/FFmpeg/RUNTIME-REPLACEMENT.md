# Replacing the External FFmpeg Runtime

PeerCast Nova uses FFmpeg only from one explicit local directory supplied by the application host. It does not search `PATH`, the Windows registry, system directories, or conventional installation locations, and it does not download or install a runtime.

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

## License requirement

The host manifest must declare exactly:

```text
LGPL-3.0-or-later
```

GPL, nonfree, unknown, undefined, or differently declared license classes are rejected by the approved production route. The application host is responsible for confirming the actual configuration and dependency licenses of the build it authorizes.

## Create a host manifest

For each of the five DLLs, calculate:

1. the exact file length in bytes;
2. the SHA-256 digest as 64 hexadecimal characters;
3. the role, exact file name, and required ABI major from the table above.

Create the manifest through `PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied`:

```csharp
var creation = PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied(
    buildId: "my.ffmpeg.8.1.modified",
    version: "8.1-custom+local1",
    licenseIdentifier: PlaybackFfmpegExternalRuntimeManifest.ApprovedLicenseIdentifier,
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

The manifest contains no directory, URL, downloader, command, script, or installer. It is only an immutable authorization record for exact local files.

## Activate through the application composition root

The application supplies the accepted manifest and explicit directory to the approved composition-root entry:

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
    },
    creation.Manifest);
```

The runtime then passes through the existing fixed-drive and reparse-point checks, directory and file guard handles, exact inventory verification, PE dependency inspection, DLL loading restrictions, ABI-major checks, and required-symbol checks. A host manifest does not disable or bypass any of those gates.

## Modifying or rebuilding FFmpeg

An interface-compatible modified LGPL shared build may be used by generating a new host manifest for the resulting DLLs. Do not reuse the built-in hashes after modifying or rebuilding a library.

PeerCast Nova does not impose a restriction on reverse engineering performed to debug modifications to the LGPL libraries. The user remains free to replace the external libraries with another compatible build that passes the explicit manifest and runtime verification contract.

## Redistribution boundary

This procedure is for a user- or host-installed runtime. It does not authorize bundling FFmpeg with PeerCast Nova.

A party that redistributes FFmpeg binaries together with Nova is the binary distributor for that package and must independently satisfy the applicable corresponding-source, build-script, patch, copyright, notice, license, installation-information, dependency, and source-availability obligations. PeerCast Nova's default release process does not perform that redistribution.
