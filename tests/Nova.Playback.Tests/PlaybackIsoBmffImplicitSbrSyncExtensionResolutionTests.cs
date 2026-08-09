namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffImplicitSbrSyncExtensionResolutionTests
{
    [Fact]
    public void Resolve_UsesExplicitImmediateOnlyPolicy()
    {
        var ga = Ga(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3),
            (0x2B7, 11), (5, 5), (1, 1), (3, 4)));
        var policy = Policy(
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyKind
                .ImmediateOnly,
            null);

        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult.Resolved,
            PlaybackIsoBmffImplicitSbrSyncExtensionResolver.Resolve(
                ga,
                policy,
                out var resolution));
        var resolved = Assert.IsType<
            PlaybackIsoBmffImplicitSbrSyncExtensionResolution>(resolution);
        Assert.Same(ga, resolved.GaSpecificConfig);
        Assert.Same(policy, resolved.Policy);
        Assert.Null(resolved.ErConfigurationAdmission);
        Assert.Null(resolved.Search);
        Assert.True(resolved.Extension.SbrPresent);
    }

    [Fact]
    public void Resolve_UsesCallerBoundedSearchPolicy()
    {
        var ga = Ga(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3),
            (0x55, 7),
            (0x2B7, 11), (5, 5), (0, 1)));
        var policy = Policy(
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyKind
                .CallerBoundedSearch,
            8);

        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult.Resolved,
            PlaybackIsoBmffImplicitSbrSyncExtensionResolver.Resolve(
                ga,
                policy,
                out var resolution));
        var resolved = Assert.IsType<
            PlaybackIsoBmffImplicitSbrSyncExtensionResolution>(resolution);
        Assert.Same(policy, resolved.Policy);
        var search = Assert.IsType<
            PlaybackIsoBmffImplicitSbrSyncExtensionSearch>(resolved.Search);
        Assert.Equal(7, search.SkippedBitCount);
        Assert.Same(search.Extension, resolved.Extension);
    }

    [Fact]
    public void ResolveAfterErAdmission_RetainsExactAdmission()
    {
        var admission =
            PlaybackIsoBmffImplicitSbrSyncExtensionSearchTests.ErAdmission(
                Bits(
                    (17, 5), (4, 4), (2, 4),
                    (0, 1), (0, 1), (0, 1), (0, 2),
                    (0x1F, 5),
                    (0x2B7, 11), (5, 5), (0, 1)));
        var policy = Policy(
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyKind
                .CallerBoundedSearch,
            6);

        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult.Resolved,
            PlaybackIsoBmffImplicitSbrSyncExtensionResolver
                .ResolveAfterErConfigurationAdmission(
                    admission,
                    policy,
                    out var resolution));
        Assert.Same(
            admission,
            Assert.IsType<
                PlaybackIsoBmffImplicitSbrSyncExtensionResolution>(resolution)
                .ErConfigurationAdmission);
    }

    [Fact]
    public void PolicyFactory_RejectsImplicitOrInconsistentBounds()
    {
        AssertPolicyResult(
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyCreationResult
                .UnexpectedMaximumSearchBitCount,
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyKind
                .ImmediateOnly,
            1);
        AssertPolicyResult(
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyCreationResult
                .InvalidMaximumSearchBitCount,
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyKind
                .CallerBoundedSearch,
            null);
        AssertPolicyResult(
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyCreationResult
                .InvalidPolicyKind,
            (PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyKind)2,
            null);
    }

    [Fact]
    public void Resolve_RequiresSourceAndPolicyAndPreservesLimitOutcome()
    {
        var policy = Policy(
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyKind
                .ImmediateOnly,
            null);
        AssertResult(
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                .MissingGaSpecificConfig,
            null,
            policy);
        AssertResult(
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                .MissingPolicy,
            Ga(Bits((2, 5), (4, 4), (2, 4), (0, 3))),
            null);

        var limited = Ga(Bits(
            (2, 5), (4, 4), (2, 4), (0, 3),
            (0x55, 7),
            (0x2B7, 11), (5, 5), (0, 1)));
        AssertResult(
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult
                .SearchLimitReached,
            limited,
            Policy(
                PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyKind
                    .CallerBoundedSearch,
                7));
    }

    private static PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicy
        Policy(
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyKind kind,
            int? maximumSearchBitCount)
    {
        Assert.Equal(
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyCreationResult
                .Created,
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyFactory
                .TryCreate(kind, maximumSearchBitCount, out var policy));
        return Assert.IsType<
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicy>(policy);
    }

    private static void AssertPolicyResult(
        PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyCreationResult
            expected,
        PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyKind kind,
        int? maximumSearchBitCount)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicyFactory
                .TryCreate(kind, maximumSearchBitCount, out var policy));
        Assert.Null(policy);
    }

    private static void AssertResult(
        PlaybackIsoBmffImplicitSbrSyncExtensionResolutionResult expected,
        PlaybackIsoBmffGaSpecificConfig? ga,
        PlaybackIsoBmffImplicitSbrSyncExtensionResolutionPolicy? policy)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffImplicitSbrSyncExtensionResolver.Resolve(
                ga,
                policy,
                out var resolution));
        Assert.Null(resolution);
    }

    private static PlaybackIsoBmffGaSpecificConfig Ga(byte[] configuration) =>
        PlaybackIsoBmffImplicitSyncExtensionParserTests.Ga(configuration);

    private static byte[] Bits(
        params (uint Value, int Count)[] fields) =>
        PlaybackIsoBmffImplicitSyncExtensionParserTests.Bits(fields);
}
