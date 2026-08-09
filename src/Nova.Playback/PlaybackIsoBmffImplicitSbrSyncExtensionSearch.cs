namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
{
    Found,
    MissingGaSpecificConfig,
    MissingErConfigurationAdmission,
    InvalidMaximumSearchBitCount,
    ExplicitExtensionAlreadyPresent,
    UnsupportedCoreAudioObjectType,
    NotFound,
    SearchLimitReached,
    NeedMoreData,
    UnsupportedExtensionAudioObjectType,
    InvalidExtensionSamplingFrequency,
}

public sealed class PlaybackIsoBmffImplicitSbrSyncExtensionSearch
{
    internal PlaybackIsoBmffImplicitSbrSyncExtensionSearch(
        PlaybackIsoBmffGaSpecificConfig gaSpecificConfig,
        PlaybackIsoBmffErConfigurationAdmission? erConfigurationAdmission,
        int searchStartBitLength,
        int markerBitPosition,
        int maximumSearchBitCount,
        PlaybackIsoBmffImplicitSbrSyncExtension extension)
    {
        GaSpecificConfig = gaSpecificConfig;
        ErConfigurationAdmission = erConfigurationAdmission;
        SearchStartBitLength = searchStartBitLength;
        MarkerBitPosition = markerBitPosition;
        MaximumSearchBitCount = maximumSearchBitCount;
        Extension = extension;
    }

    public PlaybackIsoBmffGaSpecificConfig GaSpecificConfig { get; }

    public PlaybackIsoBmffErConfigurationAdmission? ErConfigurationAdmission
    { get; }

    public int SearchStartBitLength { get; }

    public int MarkerBitPosition { get; }

    public int SkippedBitCount => MarkerBitPosition - SearchStartBitLength;

    public int MaximumSearchBitCount { get; }

    public PlaybackIsoBmffImplicitSbrSyncExtension Extension { get; }
}

public static class PlaybackIsoBmffImplicitSbrSyncExtensionSearcher
{
    private const uint SyncExtensionType = 0x2B7;

    public const int MaximumSearchBitCount =
        PlaybackBoundedDemuxOptions.MaximumCodecExtradataBytesHardLimit * 8;

    public static PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult Search(
        PlaybackIsoBmffGaSpecificConfig? gaSpecificConfig,
        int maximumSearchBitCount,
        out PlaybackIsoBmffImplicitSbrSyncExtensionSearch? search)
    {
        search = null;
        if (gaSpecificConfig is null)
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                .MissingGaSpecificConfig;
        }

        return SearchCore(
            gaSpecificConfig,
            null,
            gaSpecificConfig.ConsumedBitLength,
            requireNonErCore: true,
            maximumSearchBitCount,
            out search);
    }

    public static PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
        SearchAfterErConfigurationAdmission(
            PlaybackIsoBmffErConfigurationAdmission? admission,
            int maximumSearchBitCount,
            out PlaybackIsoBmffImplicitSbrSyncExtensionSearch? search)
    {
        search = null;
        if (admission is null)
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                .MissingErConfigurationAdmission;
        }

        return SearchCore(
            admission.Requirement.GaSpecificConfig,
            admission,
            admission.ConsumedBitLength,
            requireNonErCore: false,
            maximumSearchBitCount,
            out search);
    }

    private static PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
        SearchCore(
            PlaybackIsoBmffGaSpecificConfig gaSpecificConfig,
            PlaybackIsoBmffErConfigurationAdmission? admission,
            int searchStartBitLength,
            bool requireNonErCore,
            int maximumSearchBitCount,
            out PlaybackIsoBmffImplicitSbrSyncExtensionSearch? search)
    {
        search = null;
        if (maximumSearchBitCount is < 1 or > MaximumSearchBitCount)
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                .InvalidMaximumSearchBitCount;
        }

        if (gaSpecificConfig.ExplicitSbrPsExtension is not null)
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                .ExplicitExtensionAlreadyPresent;
        }

        if (requireNonErCore &&
            gaSpecificConfig.AudioObjectType is not (1 or 2 or 3 or 4 or 6 or 7))
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                .UnsupportedCoreAudioObjectType;
        }

        var configuration = gaSpecificConfig.AudioSpecificConfigHeader
            .ConfigurationBytes.Span;
        var totalBitLength = checked(configuration.Length * 8);
        if (searchStartBitLength < 0 || searchStartBitLength > totalBitLength)
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                .NotFound;
        }

        var availableCandidateCount = Math.Max(
            0,
            totalBitLength - searchStartBitLength - 10);
        var searchedCandidateCount = Math.Min(
            availableCandidateCount,
            maximumSearchBitCount);
        for (var offset = 0; offset < searchedCandidateCount; offset++)
        {
            var markerBitPosition = searchStartBitLength + offset;
            if (ReadBits(configuration, markerBitPosition, 11) !=
                SyncExtensionType)
            {
                continue;
            }

            var parseResult =
                PlaybackIsoBmffImplicitSbrSyncExtensionParser
                    .ParseAtBitPosition(
                        gaSpecificConfig,
                        markerBitPosition,
                        requireNonErCore,
                        out var extension);
            if (parseResult !=
                PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.Parsed)
            {
                return MapParseResult(parseResult);
            }

            search = new(
                gaSpecificConfig,
                admission,
                searchStartBitLength,
                markerBitPosition,
                maximumSearchBitCount,
                extension!);
            return PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult.Found;
        }

        return searchedCandidateCount < availableCandidateCount
            ? PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                .SearchLimitReached
            : PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult.NotFound;
    }

    private static PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
        MapParseResult(
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult result) =>
        result switch
        {
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.NeedMoreData =>
                PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                    .NeedMoreData,
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .UnsupportedExtensionAudioObjectType =>
                PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                    .UnsupportedExtensionAudioObjectType,
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .InvalidExtensionSamplingFrequency =>
                PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                    .InvalidExtensionSamplingFrequency,
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .ExplicitExtensionAlreadyPresent =>
                PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                    .ExplicitExtensionAlreadyPresent,
            PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .UnsupportedCoreAudioObjectType =>
                PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                    .UnsupportedCoreAudioObjectType,
            _ => PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult.NotFound,
        };

    private static uint ReadBits(
        ReadOnlySpan<byte> payload,
        int position,
        int bitCount)
    {
        uint value = 0;
        for (var index = 0; index < bitCount; index++)
        {
            var absoluteBit = position + index;
            var current = payload[absoluteBit / 8];
            var shift = 7 - (absoluteBit % 8);
            value = (value << 1) | (uint)((current >> shift) & 1);
        }

        return value;
    }
}
