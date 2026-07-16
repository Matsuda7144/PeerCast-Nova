namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderBackendFailure
{
    InvalidDescriptor,
    ProvenanceRejected,
    InitializationFailed,
}

public enum PlaybackDecoderBackendOrigin
{
    FirstParty,
    ThirdParty,
}

public enum PlaybackDecoderBackendDistribution
{
    BuiltIn,
    OptionalExternal,
    Bundled,
}

public enum PlaybackDecoderBackendLicenseReview
{
    Approved,
    Pending,
    Rejected,
}

public readonly record struct PlaybackDecoderBackendProvenance(
    PlaybackDecoderBackendOrigin Origin,
    PlaybackDecoderBackendDistribution Distribution,
    PlaybackDecoderBackendLicenseReview LicenseReview,
    bool ContainsNativeCode)
{
    public static PlaybackDecoderBackendProvenance FirstPartyManaged { get; } =
        new(
            PlaybackDecoderBackendOrigin.FirstParty,
            PlaybackDecoderBackendDistribution.BuiltIn,
            PlaybackDecoderBackendLicenseReview.Approved,
            ContainsNativeCode: false);
}

public sealed record PlaybackDecoderBackendArtifactManifest
{
    public PlaybackDecoderBackendArtifactManifest(
        string upstreamId,
        string version,
        string licenseId,
        string sha256)
    {
        ValidateToken(upstreamId, nameof(upstreamId));
        ValidateToken(version, nameof(version));
        ValidateToken(licenseId, nameof(licenseId));

        if (sha256.Length != 64 ||
            sha256.Any(static value => !char.IsAsciiHexDigit(value)))
        {
            throw new ArgumentException(
                "The artifact digest must be a 64-character SHA-256 hexadecimal value.",
                nameof(sha256));
        }

        UpstreamId = upstreamId;
        Version = version;
        LicenseId = licenseId;
        Sha256 = sha256.ToUpperInvariant();
    }

    public string UpstreamId { get; }

    public string Version { get; }

    public string LicenseId { get; }

    public string Sha256 { get; }

    private static void ValidateToken(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 64 || value.Any(static character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '+')))
        {
            throw new ArgumentException(
                "Manifest tokens must contain only bounded ASCII letters, digits, dots, hyphens, and plus signs.",
                parameterName);
        }
    }
}

public sealed record PlaybackDecoderBackendDescriptor
{
    private readonly PlaybackMediaContainer[] _supportedContainers;

    public PlaybackDecoderBackendDescriptor(
        string id,
        int priority,
        IEnumerable<PlaybackMediaContainer> supportedContainers,
        PlaybackDecoderBackendProvenance provenance,
        PlaybackDecoderBackendArtifactManifest? artifactManifest = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(supportedContainers);

        if (id.Length > 64 || id.Any(static value =>
                !(char.IsAsciiLetterOrDigit(value) || value is '.' or '-')))
        {
            throw new ArgumentException(
                "The backend identifier must contain only ASCII letters, digits, dots, and hyphens.",
                nameof(id));
        }

        if (priority is < -1000 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        if (!Enum.IsDefined(provenance.Origin) ||
            !Enum.IsDefined(provenance.Distribution) ||
            !Enum.IsDefined(provenance.LicenseReview))
        {
            throw new ArgumentException(
                "The backend provenance contains an undefined category.",
                nameof(provenance));
        }

        var containers = supportedContainers.Distinct().ToArray();
        if (containers.Length == 0 ||
            containers.Any(static value =>
                !Enum.IsDefined(value) || value == PlaybackMediaContainer.Unknown))
        {
            throw new ArgumentException(
                "At least one recognized container is required.",
                nameof(supportedContainers));
        }

        if ((provenance.Origin == PlaybackDecoderBackendOrigin.ThirdParty ||
                provenance.Distribution == PlaybackDecoderBackendDistribution.Bundled ||
                provenance.ContainsNativeCode) &&
            artifactManifest is null)
        {
            throw new ArgumentException(
                "Third-party, bundled, and native backends require an artifact manifest.",
                nameof(artifactManifest));
        }

        Id = id;
        Priority = priority;
        Provenance = provenance;
        ArtifactManifest = artifactManifest;
        _supportedContainers = containers;
        SupportedContainers = Array.AsReadOnly(containers);
    }

    public string Id { get; }

    public int Priority { get; }

    public IReadOnlyList<PlaybackMediaContainer> SupportedContainers { get; }

    public PlaybackDecoderBackendProvenance Provenance { get; }

    public PlaybackDecoderBackendArtifactManifest? ArtifactManifest { get; }

    public bool Supports(PlaybackMediaContainer container) =>
        Array.IndexOf(_supportedContainers, container) >= 0;
}

public sealed class PlaybackDecoderBackendException : Exception
{
    public PlaybackDecoderBackendException(
        PlaybackDecoderBackendFailure failure,
        string backendId)
        : base($"Decoder backend '{backendId}' failed with category {failure}.")
    {
        Failure = failure;
        BackendId = backendId;
    }

    public PlaybackDecoderBackendFailure Failure { get; }

    public string BackendId { get; }
}
