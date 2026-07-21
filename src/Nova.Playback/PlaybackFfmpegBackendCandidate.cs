namespace PeerCast.Nova.Playback;

/// <summary>
/// Pinned external LGPL shared-build candidate. No FFmpeg binary is bundled,
/// downloaded, installed, or redistributed by PeerCast Nova.
/// </summary>
public static class PlaybackFfmpegBackendCandidate
{
    public const string BuildId = "btbn.ffmpeg.20260715.win64.lgpl-shared";
    public const string FfmpegVersion = "n8.1.2-22-g94138f6973-20260715";
    public const string ReleaseAsset =
        "ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip";

    public static PlaybackDecoderBackendArtifactManifest ArtifactManifest { get; } =
        new(
            "btbn.ffmpeg",
            FfmpegVersion,
            "LGPL-3.0-or-later",
            "F711E0478C03FD48624C59A0487E0C5D904779A3C67280A0A7B49B0002F12B43");

    public static PlaybackDecoderBackendProvenance Provenance { get; } = new(
        PlaybackDecoderBackendOrigin.ThirdParty,
        PlaybackDecoderBackendDistribution.OptionalExternal,
        PlaybackDecoderBackendLicenseReview.Approved,
        ContainsNativeCode: true);
}
