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
    HostAttestedRuntimeNotAllowed = 32,
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

    /// <summary>
    /// Allows a host-declared, host-attested external runtime without converting
    /// that attestation into a Nova license review approval.
    /// </summary>
    public bool AllowHostAttestedRuntime { get; init; }

    public PlaybackDecoderBackendPolicyRejection Evaluate(
        PlaybackDecoderBackendProvenance provenance)
    {
        if (!IsDefined(provenance))
        {
            return PlaybackDecoderBackendPolicyRejection.InvalidProvenance;
        }

        var rejection = EvaluateRuntimeClass(provenance);
        if (provenance.LicenseReview != PlaybackDecoderBackendLicenseReview.Approved)
        {
            rejection |= PlaybackDecoderBackendPolicyRejection.LicenseNotApproved;
        }

        return rejection;
    }

    public PlaybackDecoderBackendPolicyRejection EvaluateHostAttestedRuntime(
        PlaybackDecoderBackendProvenance provenance)
    {
        if (!IsDefined(provenance) ||
            provenance.LicenseReview != PlaybackDecoderBackendLicenseReview.Pending)
        {
            return PlaybackDecoderBackendPolicyRejection.InvalidProvenance;
        }

        var rejection = EvaluateRuntimeClass(provenance);
        if (!AllowHostAttestedRuntime)
        {
            rejection |= PlaybackDecoderBackendPolicyRejection.HostAttestedRuntimeNotAllowed;
        }

        return rejection;
    }

    private PlaybackDecoderBackendPolicyRejection EvaluateRuntimeClass(
        PlaybackDecoderBackendProvenance provenance)
    {
        var rejection = PlaybackDecoderBackendPolicyRejection.None;
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

    private static bool IsDefined(PlaybackDecoderBackendProvenance provenance) =>
        Enum.IsDefined(provenance.Origin) &&
        Enum.IsDefined(provenance.Distribution) &&
        Enum.IsDefined(provenance.LicenseReview);
}
