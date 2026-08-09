namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffImplicitSbrSyncExtensionSearchTests
{
    [Fact]
    public void Search_FindsNonImmediateMarkerWithExactBitProvenance()
    {
        var ga = Ga(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3),
            (0x55, 7),
            (0x2B7, 11), (5, 5), (1, 1), (3, 4),
            (0x548, 11), (1, 1)));

        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult.Found,
            PlaybackIsoBmffImplicitSbrSyncExtensionSearcher.Search(
                ga,
                8,
                out var search));
        var found = Assert.IsType<
            PlaybackIsoBmffImplicitSbrSyncExtensionSearch>(search);
        Assert.Same(ga, found.GaSpecificConfig);
        Assert.Null(found.ErConfigurationAdmission);
        Assert.Equal(16, found.SearchStartBitLength);
        Assert.Equal(23, found.MarkerBitPosition);
        Assert.Equal(7, found.SkippedBitCount);
        Assert.Equal(8, found.MaximumSearchBitCount);
        Assert.True(found.Extension.SbrPresent);
        Assert.Equal(48_000u, found.Extension.ExtensionSamplingFrequency);
        Assert.Equal(44, found.Extension.ConsumedBitLength);
        Assert.Equal(
            PlaybackIsoBmffImplicitPsSyncExtensionParseResult.Parsed,
            PlaybackIsoBmffImplicitPsSyncExtensionParser.Parse(
                found.Extension,
                out var ps));
        Assert.True(ps!.PsPresent);
        Assert.Equal(56, ps.ConsumedBitLength);
    }

    [Fact]
    public void SearchAfterErAdmission_FindsNonImmediateMarker()
    {
        var admission = ErAdmission(Bits(
            (17, 5), (4, 4), (2, 4),
            (0, 1), (0, 1), (0, 1), (0, 2),
            (0x1F, 5),
            (0x2B7, 11), (5, 5), (0, 1)));

        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult.Found,
            PlaybackIsoBmffImplicitSbrSyncExtensionSearcher
                .SearchAfterErConfigurationAdmission(
                    admission,
                    6,
                    out var search));
        var found = Assert.IsType<
            PlaybackIsoBmffImplicitSbrSyncExtensionSearch>(search);
        Assert.Same(admission, found.ErConfigurationAdmission);
        Assert.Same(
            admission.Requirement.GaSpecificConfig,
            found.GaSpecificConfig);
        Assert.Equal(admission.ConsumedBitLength, found.SearchStartBitLength);
        Assert.Equal(5, found.SkippedBitCount);
        Assert.False(found.Extension.SbrPresent);
    }

    [Fact]
    public void Search_DistinguishesLimitFromExhaustion()
    {
        var limited = Ga(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3),
            (0x55, 7),
            (0x2B7, 11), (5, 5), (0, 1)));
        var absent = Ga(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3),
            (0, 16)));

        AssertResult(
            PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                .SearchLimitReached,
            limited,
            7);
        AssertResult(
            PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult.NotFound,
            absent,
            64);
    }

    [Fact]
    public void Search_RejectsMissingSourceAndInvalidBound()
    {
        AssertResult(
            PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                .MissingGaSpecificConfig,
            null,
            1);
        AssertResult(
            PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                .InvalidMaximumSearchBitCount,
            Ga(Bits((2, 5), (4, 4), (2, 4), (0, 3))),
            0);
        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult
                .MissingErConfigurationAdmission,
            PlaybackIsoBmffImplicitSbrSyncExtensionSearcher
                .SearchAfterErConfigurationAdmission(
                    null,
                    1,
                    out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void Search_ReportsTruncatedMarkerPayload()
    {
        AssertResult(
            PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult.NeedMoreData,
            Ga(Bits(
                (2, 5), (4, 4), (2, 4), (0, 3),
                (0x2B7, 11), (5, 5))),
            1);
    }

    private static void AssertResult(
        PlaybackIsoBmffImplicitSbrSyncExtensionSearchResult expected,
        PlaybackIsoBmffGaSpecificConfig? ga,
        int maximumSearchBitCount)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffImplicitSbrSyncExtensionSearcher.Search(
                ga,
                maximumSearchBitCount,
                out var search));
        Assert.Null(search);
    }

    internal static PlaybackIsoBmffErConfigurationAdmission ErAdmission(
        byte[] configuration)
    {
        var ga = Ga(configuration);
        Assert.Equal(
            PlaybackIsoBmffErAudioProtectionRequirementResult.Projected,
            PlaybackIsoBmffErAudioProtectionRequirementProjector.Project(
                ga,
                out var requirement));
        Assert.Equal(
            PlaybackIsoBmffErConfigurationAdmissionResult.Admitted,
            PlaybackIsoBmffErConfigurationAdmissionResolver.Resolve(
                requirement,
                null,
                null,
                out var admission));
        return Assert.IsType<PlaybackIsoBmffErConfigurationAdmission>(
            admission);
    }

    private static PlaybackIsoBmffGaSpecificConfig Ga(byte[] configuration) =>
        PlaybackIsoBmffImplicitSyncExtensionParserTests.Ga(configuration);

    private static byte[] Bits(
        params (uint Value, int Count)[] fields) =>
        PlaybackIsoBmffImplicitSyncExtensionParserTests.Bits(fields);
}
