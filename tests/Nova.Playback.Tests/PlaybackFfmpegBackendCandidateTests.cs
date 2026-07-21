namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFfmpegBackendCandidateTests
{
    [Fact]
    public void Candidate_IsPinnedApprovedAndRequiresExplicitExternalNativePolicy()
    {
        var manifest = PlaybackFfmpegBackendCandidate.ArtifactManifest;
        var provenance = PlaybackFfmpegBackendCandidate.Provenance;
        var approvedPolicy = new PlaybackDecoderBackendPolicy
        {
            AllowThirdParty = true,
            AllowNativeCode = true,
            AllowBundled = false,
        };
        var defaultPolicy = new PlaybackDecoderBackendPolicy();

        Assert.Equal("btbn.ffmpeg", manifest.UpstreamId);
        Assert.Equal("n8.1.2-29-g703dcc25b9-20260721", manifest.Version);
        Assert.Equal("btbn.ffmpeg.20260721.win64.lgpl-shared", PlaybackFfmpegBackendCandidate.BuildId);
        Assert.Equal(70_510_808, PlaybackFfmpegBackendCandidate.ArchiveSize);
        Assert.Equal(
            "50764B52D38CB0BAF8AF938C0E4CA886D2C6753B520B2C319B532ED5C17E7CBF",
            manifest.Sha256);
        Assert.Equal("LGPL-3.0-or-later", manifest.LicenseId);
        Assert.Equal(64, manifest.Sha256.Length);
        Assert.Equal(
            PlaybackDecoderBackendLicenseReview.Approved,
            provenance.LicenseReview);
        Assert.Equal(
            PlaybackDecoderBackendDistribution.OptionalExternal,
            provenance.Distribution);
        Assert.Equal(
            PlaybackDecoderBackendPolicyRejection.None,
            approvedPolicy.Evaluate(provenance));
        Assert.Equal(
            PlaybackDecoderBackendPolicyRejection.ThirdPartyNotAllowed |
                PlaybackDecoderBackendPolicyRejection.NativeCodeNotAllowed,
            defaultPolicy.Evaluate(provenance));
    }

    [Fact]
    public void Superseded_candidate_identity_is_retained_without_approval_reuse()
    {
        var superseded = Assert.Single(PlaybackFfmpegBackendCandidate.SupersededCandidates);

        Assert.Equal("btbn.ffmpeg.20260715.win64.lgpl-shared", superseded.BuildId);
        Assert.Equal("n8.1.2-22-g94138f6973-20260715", superseded.FfmpegVersion);
        Assert.Equal("autobuild-2026-07-15-14-01", superseded.ReleaseTag);
        Assert.Equal(
            "F711E0478C03FD48624C59A0487E0C5D904779A3C67280A0A7B49B0002F12B43",
            superseded.ArchiveSha256);
        Assert.NotEqual(PlaybackFfmpegBackendCandidate.BuildId, superseded.BuildId);
        Assert.NotEqual(PlaybackFfmpegBackendCandidate.ArchiveSha256, superseded.ArchiveSha256);
    }

    [Fact]
    public async Task CandidateManifest_VerifiesObservedReleaseBytes()
    {
        var bytes = Convert.FromHexString(
            "504B0304140000000000000000000000000000000000000000");
        await using var stream = new MemoryStream(bytes);

        var result = await PlaybackDecoderBackendArtifactVerifier.VerifyAsync(
            stream,
            PlaybackFfmpegBackendCandidate.ArtifactManifest);

        Assert.Equal(
            PlaybackDecoderBackendArtifactVerification.DigestMismatch,
            result);
    }
}
