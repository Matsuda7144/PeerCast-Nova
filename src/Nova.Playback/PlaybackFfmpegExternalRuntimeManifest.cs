namespace PeerCast.Nova.Playback;

public enum PlaybackFfmpegExternalRuntimeManifestOrigin
{
    BuiltInCandidate,
    HostSupplied,
}

public enum PlaybackFfmpegExternalRuntimeLicenseDeclarationAuthority
{
    ProjectReviewed,
    HostDeclared,
}

public enum PlaybackFfmpegExternalRuntimeAuthorization
{
    BuiltInApproved,
    HostAttested,
}

public enum PlaybackFfmpegExternalRuntimeReview
{
    Approved,
    Unreviewed,
}

public enum PlaybackFfmpegExternalRuntimeLibraryRole
{
    AvCodec,
    AvFormat,
    AvUtil,
    SwResample,
    SwScale,
}

public enum PlaybackFfmpegExternalRuntimeManifestResult
{
    Accepted,
    InvalidBuildId,
    InvalidVersion,
    LicenseRejected,
    ConfigurationRejected,
    InvalidFileCount,
    UndefinedRole,
    DuplicateRole,
    MissingRole,
    FileNameRejected,
    AbiMajorRejected,
    FileLengthRejected,
    DigestRejected,
    InvalidInventory,
}

public readonly record struct PlaybackFfmpegExternalRuntimeBuildConfiguration(
    bool GplEnabled,
    bool NonfreeEnabled,
    bool SharedEnabled,
    bool StaticEnabled)
{
    public static PlaybackFfmpegExternalRuntimeBuildConfiguration Required { get; } =
        new(
            GplEnabled: false,
            NonfreeEnabled: false,
            SharedEnabled: true,
            StaticEnabled: false);
}

public readonly record struct PlaybackFfmpegExternalRuntimeFile(
    PlaybackFfmpegExternalRuntimeLibraryRole Role,
    string FileName,
    int AbiMajor,
    long Length,
    string Sha256);

public sealed record PlaybackFfmpegExternalRuntimeManifestCreation(
    PlaybackFfmpegExternalRuntimeManifestResult Result,
    PlaybackFfmpegExternalRuntimeManifest? Manifest);

/// <summary>
/// Immutable authorization record for one external LGPL FFmpeg shared runtime.
/// A built-in manifest records a Nova review; a host manifest records only the
/// host's declaration and attestation. It carries no directory, URL, command,
/// archive, download, or installation data.
/// </summary>
public sealed class PlaybackFfmpegExternalRuntimeManifest
{
    public const string RequiredDeclaredLicenseIdentifier = "LGPL-3.0-or-later";
    public const int MaximumIdentityLength = 128;
    public const int RequiredFileCount = 5;

    private static readonly PlaybackFfmpegExternalRuntimeLibraryRole[] RequiredRoles =
    [
        PlaybackFfmpegExternalRuntimeLibraryRole.AvCodec,
        PlaybackFfmpegExternalRuntimeLibraryRole.AvFormat,
        PlaybackFfmpegExternalRuntimeLibraryRole.AvUtil,
        PlaybackFfmpegExternalRuntimeLibraryRole.SwResample,
        PlaybackFfmpegExternalRuntimeLibraryRole.SwScale,
    ];

    private readonly PlaybackFfmpegExternalRuntimeFile[] _files;

    private PlaybackFfmpegExternalRuntimeManifest(
        PlaybackFfmpegExternalRuntimeManifestOrigin origin,
        string buildId,
        string version,
        PlaybackFfmpegExternalRuntimeBuildConfiguration buildConfiguration,
        PlaybackFfmpegExternalRuntimeFile[] files,
        PlaybackDecoderBackendRuntimeInventory runtimeInventory)
    {
        Origin = origin;
        BuildId = buildId;
        Version = version;
        LicenseIdentifier = RequiredDeclaredLicenseIdentifier;
        BuildConfiguration = buildConfiguration;
        _files = files;
        Files = Array.AsReadOnly(_files);
        RuntimeInventory = runtimeInventory;

        var isBuiltIn = origin == PlaybackFfmpegExternalRuntimeManifestOrigin.BuiltInCandidate;
        LicenseDeclarationAuthority = isBuiltIn
            ? PlaybackFfmpegExternalRuntimeLicenseDeclarationAuthority.ProjectReviewed
            : PlaybackFfmpegExternalRuntimeLicenseDeclarationAuthority.HostDeclared;
        Authorization = isBuiltIn
            ? PlaybackFfmpegExternalRuntimeAuthorization.BuiltInApproved
            : PlaybackFfmpegExternalRuntimeAuthorization.HostAttested;
        Review = isBuiltIn
            ? PlaybackFfmpegExternalRuntimeReview.Approved
            : PlaybackFfmpegExternalRuntimeReview.Unreviewed;
        Provenance = new(
            PlaybackDecoderBackendOrigin.ThirdParty,
            PlaybackDecoderBackendDistribution.OptionalExternal,
            isBuiltIn
                ? PlaybackDecoderBackendLicenseReview.Approved
                : PlaybackDecoderBackendLicenseReview.Pending,
            ContainsNativeCode: true);
    }

    public static PlaybackFfmpegExternalRuntimeManifest BuiltInCandidate { get; } =
        CreateBuiltInCandidate();

    public PlaybackFfmpegExternalRuntimeManifestOrigin Origin { get; }

    public PlaybackFfmpegExternalRuntimeLicenseDeclarationAuthority LicenseDeclarationAuthority
    {
        get;
    }

    public PlaybackFfmpegExternalRuntimeAuthorization Authorization { get; }

    public PlaybackFfmpegExternalRuntimeReview Review { get; }

    public string BuildId { get; }

    public string Version { get; }

    public string LicenseIdentifier { get; }

    public PlaybackFfmpegExternalRuntimeBuildConfiguration BuildConfiguration { get; }

    public bool IsHostSupplied => Origin == PlaybackFfmpegExternalRuntimeManifestOrigin.HostSupplied;

    public IReadOnlyList<PlaybackFfmpegExternalRuntimeFile> Files { get; }

    public PlaybackDecoderBackendProvenance Provenance { get; }

    internal PlaybackDecoderBackendRuntimeInventory RuntimeInventory { get; }

    public static PlaybackFfmpegExternalRuntimeManifestCreation CreateHostSupplied(
        string buildId,
        string version,
        string licenseIdentifier,
        PlaybackFfmpegExternalRuntimeBuildConfiguration buildConfiguration,
        IEnumerable<PlaybackFfmpegExternalRuntimeFile> files) =>
        Create(
            PlaybackFfmpegExternalRuntimeManifestOrigin.HostSupplied,
            buildId,
            version,
            licenseIdentifier,
            buildConfiguration,
            files,
            runtimeInventory: null);

    private static PlaybackFfmpegExternalRuntimeManifestCreation Create(
        PlaybackFfmpegExternalRuntimeManifestOrigin origin,
        string buildId,
        string version,
        string licenseIdentifier,
        PlaybackFfmpegExternalRuntimeBuildConfiguration buildConfiguration,
        IEnumerable<PlaybackFfmpegExternalRuntimeFile> files,
        PlaybackDecoderBackendRuntimeInventory? runtimeInventory)
    {
        if (!IsBoundedIdentity(buildId))
        {
            return Rejected(PlaybackFfmpegExternalRuntimeManifestResult.InvalidBuildId);
        }

        if (!IsBoundedIdentity(version))
        {
            return Rejected(PlaybackFfmpegExternalRuntimeManifestResult.InvalidVersion);
        }

        if (!string.Equals(
                licenseIdentifier,
                RequiredDeclaredLicenseIdentifier,
                StringComparison.Ordinal))
        {
            return Rejected(PlaybackFfmpegExternalRuntimeManifestResult.LicenseRejected);
        }

        if (buildConfiguration != PlaybackFfmpegExternalRuntimeBuildConfiguration.Required)
        {
            return Rejected(PlaybackFfmpegExternalRuntimeManifestResult.ConfigurationRejected);
        }

        if (files is null)
        {
            return Rejected(PlaybackFfmpegExternalRuntimeManifestResult.InvalidFileCount);
        }

        PlaybackFfmpegExternalRuntimeFile[] materialized;
        try
        {
            materialized = files.Take(RequiredFileCount + 1).ToArray();
        }
        catch
        {
            return Rejected(PlaybackFfmpegExternalRuntimeManifestResult.InvalidInventory);
        }

        if (materialized.Length != RequiredFileCount)
        {
            return Rejected(PlaybackFfmpegExternalRuntimeManifestResult.InvalidFileCount);
        }

        if (Array.Exists(materialized, static file => !Enum.IsDefined(file.Role)))
        {
            return Rejected(PlaybackFfmpegExternalRuntimeManifestResult.UndefinedRole);
        }

        if (materialized.GroupBy(static file => file.Role)
            .Any(static group => group.Count() > 1))
        {
            return Rejected(PlaybackFfmpegExternalRuntimeManifestResult.DuplicateRole);
        }

        if (Array.Exists(
                RequiredRoles,
                role => Array.TrueForAll(materialized, file => file.Role != role)))
        {
            return Rejected(PlaybackFfmpegExternalRuntimeManifestResult.MissingRole);
        }

        foreach (var file in materialized)
        {
            var expected = Expected(file.Role);
            if (!string.Equals(file.FileName, expected.FileName, StringComparison.Ordinal))
            {
                return Rejected(PlaybackFfmpegExternalRuntimeManifestResult.FileNameRejected);
            }

            if (file.AbiMajor != expected.AbiMajor)
            {
                return Rejected(PlaybackFfmpegExternalRuntimeManifestResult.AbiMajorRejected);
            }

            if (file.Length <= 0)
            {
                return Rejected(PlaybackFfmpegExternalRuntimeManifestResult.FileLengthRejected);
            }

            if (file.Sha256 is null || file.Sha256.Length != 64 ||
                !file.Sha256.All(static value => char.IsAsciiHexDigit(value)))
            {
                return Rejected(PlaybackFfmpegExternalRuntimeManifestResult.DigestRejected);
            }
        }

        materialized = materialized
            .OrderBy(static file => file.Role)
            .Select(static file => file with { Sha256 = file.Sha256.ToUpperInvariant() })
            .ToArray();

        try
        {
            var requiredInventory = new PlaybackDecoderBackendRuntimeInventory(materialized.Select(
                static file => new PlaybackDecoderBackendRuntimeFile(
                    file.FileName,
                    file.Length,
                    file.Sha256)));
            var inventory = runtimeInventory ?? requiredInventory;
            var inventoryByName = inventory.Files.ToDictionary(
                static file => file.Name,
                StringComparer.OrdinalIgnoreCase);
            if (requiredInventory.Files.Any(required =>
                    !inventoryByName.TryGetValue(required.Name, out var actual) ||
                    actual.Length != required.Length ||
                    !string.Equals(
                        actual.Sha256,
                        required.Sha256,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return Rejected(PlaybackFfmpegExternalRuntimeManifestResult.InvalidInventory);
            }

            return new(
                PlaybackFfmpegExternalRuntimeManifestResult.Accepted,
                new PlaybackFfmpegExternalRuntimeManifest(
                    origin,
                    buildId,
                    version,
                    buildConfiguration,
                    materialized,
                    inventory));
        }
        catch
        {
            return Rejected(PlaybackFfmpegExternalRuntimeManifestResult.InvalidInventory);
        }
    }

    private static PlaybackFfmpegExternalRuntimeManifest CreateBuiltInCandidate()
    {
        var inventory = PlaybackFfmpegBackendRuntimeInventory.PinnedCandidate;
        var creation = Create(
            PlaybackFfmpegExternalRuntimeManifestOrigin.BuiltInCandidate,
            PlaybackFfmpegBackendCandidate.BuildId,
            PlaybackFfmpegBackendCandidate.FfmpegVersion,
            RequiredDeclaredLicenseIdentifier,
            PlaybackFfmpegExternalRuntimeBuildConfiguration.Required,
            RequiredRoles.Select(role =>
            {
                var expected = Expected(role);
                var file = inventory.Files.Single(candidate => string.Equals(
                    candidate.Name,
                    expected.FileName,
                    StringComparison.Ordinal));
                return new PlaybackFfmpegExternalRuntimeFile(
                    role,
                    file.Name,
                    expected.AbiMajor,
                    file.Length,
                    file.Sha256);
            }),
            inventory);
        return creation.Manifest ??
            throw new InvalidOperationException("The built-in FFmpeg runtime manifest is invalid.");
    }

    private static (string FileName, int AbiMajor) Expected(
        PlaybackFfmpegExternalRuntimeLibraryRole role) => role switch
        {
            PlaybackFfmpegExternalRuntimeLibraryRole.AvCodec => ("avcodec-62.dll", 62),
            PlaybackFfmpegExternalRuntimeLibraryRole.AvFormat => ("avformat-62.dll", 62),
            PlaybackFfmpegExternalRuntimeLibraryRole.AvUtil => ("avutil-60.dll", 60),
            PlaybackFfmpegExternalRuntimeLibraryRole.SwResample => ("swresample-6.dll", 6),
            PlaybackFfmpegExternalRuntimeLibraryRole.SwScale => ("swscale-9.dll", 9),
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

    private static bool IsBoundedIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumIdentityLength &&
        value.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or '+');

    private static PlaybackFfmpegExternalRuntimeManifestCreation Rejected(
        PlaybackFfmpegExternalRuntimeManifestResult result) => new(result, null);
}
