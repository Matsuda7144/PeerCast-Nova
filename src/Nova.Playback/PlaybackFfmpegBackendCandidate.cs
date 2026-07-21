namespace PeerCast.Nova.Playback;

public sealed record PlaybackFfmpegSupersededCandidate(
    string BuildId,
    string FfmpegVersion,
    string ReleaseTag,
    string ReleaseAsset,
    string BtbNCommit,
    string FfmpegCommit,
    string ArchiveSha256);

/// <summary>
/// Pinned external LGPL shared-build candidate. No FFmpeg binary is bundled,
/// downloaded, installed, or redistributed by PeerCast Nova.
/// </summary>
public static class PlaybackFfmpegBackendCandidate
{
    public const string BuildRepository = "BtbN/FFmpeg-Builds";
    public const string BuildId = "btbn.ffmpeg.20260721.win64.lgpl-shared";
    public const string FfmpegVersion = "n8.1.2-29-g703dcc25b9-20260721";
    public const string ReleaseTag = "autobuild-2026-07-21-13-38";
    public const string ReleaseTime = "2026-07-21T13:38:40Z";
    public const string ReleaseAsset =
        "ffmpeg-n8.1.2-29-g703dcc25b9-win64-lgpl-shared-8.1.zip";
    public const long ArchiveSize = 70_510_808;
    public const string ArchiveSha256 =
        "50764B52D38CB0BAF8AF938C0E4CA886D2C6753B520B2C319B532ED5C17E7CBF";
    public const string BtbNCommit =
        "8c736b2d6fe5da2a10a8896d01e53bfb0ca4f665";
    public const string FfmpegCommit =
        "703dcc25b91eacd2ab8b8b2fe888dc8d7ab4ad6d";

    public static IReadOnlyList<PlaybackFfmpegSupersededCandidate> SupersededCandidates
    { get; } = Array.AsReadOnly<PlaybackFfmpegSupersededCandidate>(
    [
        new(
            "btbn.ffmpeg.20260715.win64.lgpl-shared",
            "n8.1.2-22-g94138f6973-20260715",
            "autobuild-2026-07-15-14-01",
            "ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip",
            "1f74efed63f467dbf0d1e5dd8548bf2188f4ad21",
            "94138f6973dd1ac6208ace92148ac0d172455d65",
            "F711E0478C03FD48624C59A0487E0C5D904779A3C67280A0A7B49B0002F12B43"),
    ]);

    public static PlaybackDecoderBackendArtifactManifest ArtifactManifest { get; } =
        new(
            "btbn.ffmpeg",
            FfmpegVersion,
            "LGPL-3.0-or-later",
            ArchiveSha256);

    public static PlaybackDecoderBackendProvenance Provenance { get; } = new(
        PlaybackDecoderBackendOrigin.ThirdParty,
        PlaybackDecoderBackendDistribution.OptionalExternal,
        PlaybackDecoderBackendLicenseReview.Approved,
        ContainsNativeCode: true);
}
