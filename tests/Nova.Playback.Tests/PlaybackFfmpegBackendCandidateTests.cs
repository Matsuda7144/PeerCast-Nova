namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFfmpegBackendCandidateTests
{
    [Fact]
    public void Candidate_IsPinnedAndCannotBeActivatedWhileReviewIsPending()
    {
        var manifest = PlaybackFfmpegBackendCandidate.ArtifactManifest;
        var provenance = PlaybackFfmpegBackendCandidate.Provenance;
        var permissivePolicy = new PlaybackDecoderBackendPolicy
        {
            AllowThirdParty = true,
            AllowNativeCode = true,
        };

        Assert.Equal("btbn.ffmpeg", manifest.UpstreamId);
        Assert.Equal("n8.1.2-22-g94138f6973-20260715", manifest.Version);
        Assert.Equal("LGPL-3.0-or-later", manifest.LicenseId);
        Assert.Equal(64, manifest.Sha256.Length);
        Assert.Equal(
            PlaybackDecoderBackendLicenseReview.Pending,
            provenance.LicenseReview);
        Assert.Equal(
            PlaybackDecoderBackendPolicyRejection.LicenseNotApproved,
            permissivePolicy.Evaluate(provenance));
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
