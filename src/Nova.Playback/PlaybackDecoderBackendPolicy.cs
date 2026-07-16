namespace PeerCast.Nova.Playback;

[Flags]
public enum PlaybackDecoderBackendPolicyRejection
{
    None = 0,
    LicenseNotApproved = 1,
    ThirdPartyNotAllowed = 2,
    BundledNotAllowed = 4,
    NativeCodeNotAllowed = 8,
    InvalidProvenance = 16,
}

public readonly record struct PlaybackDecoderBackendAuditEntry(
    string BackendId,
    PlaybackDecoderBackendPolicyRejection Rejection)
{
    public bool IsAllowed => Rejection == PlaybackDecoderBackendPolicyRejection.None;
}

public sealed record PlaybackDecoderBackendPolicy
{
    public bool AllowThirdParty { get; init; }

    public bool AllowBundled { get; init; }

    public bool AllowNativeCode { get; init; }

    public PlaybackDecoderBackendPolicyRejection Evaluate(
        PlaybackDecoderBackendProvenance provenance)
    {
        if (!Enum.IsDefined(provenance.Origin) ||
            !Enum.IsDefined(provenance.Distribution) ||
            !Enum.IsDefined(provenance.LicenseReview))
        {
            return PlaybackDecoderBackendPolicyRejection.InvalidProvenance;
        }

        var rejection = PlaybackDecoderBackendPolicyRejection.None;
        if (provenance.LicenseReview != PlaybackDecoderBackendLicenseReview.Approved)
        {
            rejection |= PlaybackDecoderBackendPolicyRejection.LicenseNotApproved;
        }

        if (provenance.Origin == PlaybackDecoderBackendOrigin.ThirdParty && !AllowThirdParty)
        {
            rejection |= PlaybackDecoderBackendPolicyRejection.ThirdPartyNotAllowed;
        }

        if (provenance.Distribution == PlaybackDecoderBackendDistribution.Bundled && !AllowBundled)
        {
            rejection |= PlaybackDecoderBackendPolicyRejection.BundledNotAllowed;
        }

        if (provenance.ContainsNativeCode && !AllowNativeCode)
        {
            rejection |= PlaybackDecoderBackendPolicyRejection.NativeCodeNotAllowed;
        }

        return rejection;
    }
}
