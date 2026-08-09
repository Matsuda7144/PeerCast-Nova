namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyKind
{
    ImmediateOnly,
    CallerBoundedSearch,
}

public enum
    PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyCreationResult
{
    Created,
    InvalidPolicyKind,
    UnexpectedMaximumSearchBitCount,
    InvalidMaximumSearchBitCount,
}

public sealed class PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicy
{
    internal PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicy(
        PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyKind kind,
        int? maximumSearchBitCount)
    {
        Kind = kind;
        MaximumSearchBitCount = maximumSearchBitCount;
    }

    public PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyKind Kind
    { get; }

    public int? MaximumSearchBitCount { get; }
}

public static class
    PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyFactory
{
    public static
        PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyCreationResult
        TryCreate(
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyKind kind,
            int? maximumSearchBitCount,
            out PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicy?
                policy)
    {
        policy = null;
        if (!Enum.IsDefined(kind))
        {
            return
                PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyCreationResult
                    .InvalidPolicyKind;
        }

        if (kind ==
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyKind
                .ImmediateOnly)
        {
            if (maximumSearchBitCount is not null)
            {
                return
                    PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyCreationResult
                        .UnexpectedMaximumSearchBitCount;
            }
        }
        else if (maximumSearchBitCount is not int maximum ||
            maximum is < 1 or >
                PlaybackIsoBmffImplicitSbrSyncExtensionSearcher
                    .MaximumSearchBitCount)
        {
            return
                PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyCreationResult
                    .InvalidMaximumSearchBitCount;
        }

        policy = new(kind, maximumSearchBitCount);
        return
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyCreationResult
                .Created;
    }
}

public enum PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
{
    Resolved,
    MissingGaSpecificConfig,
    MissingErConfigurationAdmission,
    MissingPolicy,
    InvalidPolicy,
    ExplicitExtensionAlreadyPresent,
    UnsupportedCoreAudioObjectType,
    NotPresent,
    SearchLimitReached,
    NeedMoreData,
    UnsupportedExtensionAudioObjectType,
    InvalidExtensionSamplingFrequency,
}

public sealed class PlaybackIsoBmffImplicitSbrSyncExtensionResolution
{
    internal PlaybackIsoBmffImplicitSbrSyncExtensionResolution(
        PlaybackIsoBmffGaSpecificConfig gaSpecificConfig,
        PlaybackIsoBmffErConfigurationAdmission? erConfigurationAdmission,
        PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicy policy,
        PlaybackIsoBmffImplicitSbrSyncExtension extension,
        PlaybackIsoBmffImplicitSbrSyncExtensionSearch? search)
    {
        GaSpecificConfig = gaSpecificConfig;
        ErConfigurationAdmission = erConfigurationAdmission;
        Policy = policy;
        Extension = extension;
        Search = search;
    }

    public PlaybackIsoBmffGaSpecificConfig GaSpecificConfig { get; }

    public PlaybackIsoBmffErConfigurationAdmission? ErConfigurationAdmission
    { get; }

    public PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicy Policy
    { get; }

    public PlaybackIsoBmffImplicitSbrSyncExtension Extension { get; }

    public PlaybackIsoBmffImplicitSbrSyncExtensionSearch? Search { get; }
}

public static class PlaybackIsoBmffImplicitSbrSyncExtensionResolver
{
    public static PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
        Resolve(
            PlaybackIsoBmffGaSpecificConfig? gaSpecificConfig,
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicy? policy,
            out PlaybackIsoBmffImplicitSbrSyncExtensionResolution? resolution)
    {
        resolution = null;
        if (gaSpecificConfig is null)
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                .MissingGaSpecificConfig;
        }

        return ResolveCore(
            gaSpecificConfig,
            null,
            policy,
            out resolution);
    }

    public static PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
        ResolveAfterErConfigurationAdmission(
            PlaybackIsoBmffErConfigurationAdmission? admission,
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicy? policy,
            out PlaybackIsoBmffImplicitSbrSyncExtensionResolution? resolution)
    {
        resolution = null;
        if (admission is null)
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                .MissingErConfigurationAdmission;
        }

        return ResolveCore(
            admission.Requirement.GaSpecificConfig,
            admission,
            policy,
            out resolution);
    }

    private static PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
        ResolveCore(
            PlaybackIsoBmffGaSpecificConfig gaSpecificConfig,
            PlaybackIsoBmffErConfigurationAdmission? admission,
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicy? policy,
            out PlaybackIsoBmffImplicitSbrSyncExtensionResolution? resolution)
    {
        resolution = null;
        if (policy is null)
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                .MissingPolicy;
        }

        if (policy.Kind ==
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyKind
                .ImmediateOnly)
        {
            if (policy.MaximumSearchBitCount is not null)
            {
                return PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                    .InvalidPolicy;
            }

            var result = admission is null
                ? PlaybackIsoBmffImplicitSbrSyncExtensionParser.Parse(
                    gaSpecificConfig,
                    out var extension)
                : PlaybackIsoBmffImplicitSbrSyncExtensionParser
                    .ParseAfterErConfigurationAdmission(
                        admission,
                        out extension);
            if (result !=
                PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.Parsed)
            {
                return MapParseResult(result);
            }

            resolution = new(
                gaSpecificConfig,
                admission,
                policy,
                extension!,
                null);
            return PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                .Resolved;
        }

        if (policy.Kind !=
                PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyKind
                    .CallerBoundedSearch ||
            policy.MaximumSearchBitCount is not int maximumSearchBitCount ||
            maximumSearchBitCount is < 1 or >
                PlaybackIsoBmffImplicitSbrSyncExtensionSearcher
                    .MaximumSearchBitCount)
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                .InvalidPolicy;
        }

        var searchResult = admission is null
            ? PlaybackIsoBmffImplicitSbrSyncExtensionSearcher.Search(
                gaSpecificConfig,
                maximumSearchBitCount,
                out var search)
            : PlaybackIsoBmffImplicitSbrSyncExtensionSearcher
                .SearchAfterErConfigurationAdmission(
                    admission,
                    maximumSearchBitCount,
                    out search);
        if (searchResult !=
            PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult.Found)
        {
            return MapSearchResult(searchResult);
        }

        resolution = new(
            gaSpecificConfig,
            admission,
            policy,
            search!.Extension,
            search);
        return PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult.Resolved;
    }

    private static PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
        MapParseResult(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult result) =>
        result switch
        {
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .ExplicitExtensionAlreadyPresent =>
                PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                    .ExplicitExtensionAlreadyPresent,
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .UnsupportedCoreAudioObjectType =>
                PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                    .UnsupportedCoreAudioObjectType,
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.NeedMoreData =>
                PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                    .NeedMoreData,
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .UnsupportedExtensionAudioObjectType =>
                PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                    .UnsupportedExtensionAudioObjectType,
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .InvalidExtensionSamplingFrequency =>
                PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                    .InvalidExtensionSamplingFrequency,
            _ => PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                .NotPresent,
        };

    private static PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
        MapSearchResult(
            PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult result) =>
        result switch
        {
            PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                .ExplicitExtensionAlreadyPresent =>
                PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                    .ExplicitExtensionAlreadyPresent,
            PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                .UnsupportedCoreAudioObjectType =>
                PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                    .UnsupportedCoreAudioObjectType,
            PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                .SearchLimitReached =>
                PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                    .SearchLimitReached,
            PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult.NeedMoreData =>
                PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                    .NeedMoreData,
            PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                .UnsupportedExtensionAudioObjectType =>
                PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                    .UnsupportedExtensionAudioObjectType,
            PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                .InvalidExtensionSamplingFrequency =>
                PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                    .InvalidExtensionSamplingFrequency,
            _ => PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                .NotPresent,
        };
}
