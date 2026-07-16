using System.Reflection;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderBackendBoundaryTests
{
    [Fact]
    public void Construction_DoesNotInitializeBackends()
    {
        var backend = new FakeBackend("fake", 0, PlaybackMediaContainer.FlashVideo);

        var factory = new PlaybackDecoderBackendFactory([backend]);

        Assert.Empty(backend.Contexts);
        Assert.Single(factory.Backends);
    }

    [Fact]
    public void Create_SelectsHighestPrioritySupportingBackend()
    {
        var lower = new FakeBackend("lower", 1, PlaybackMediaContainer.FlashVideo);
        var higher = new FakeBackend("higher", 2, PlaybackMediaContainer.FlashVideo);
        var factory = new PlaybackDecoderBackendFactory([lower, higher]);

        var decoder = factory.Create(CreateContext(PlaybackMediaContainer.FlashVideo));

        Assert.NotNull(decoder);
        Assert.Empty(lower.Contexts);
        Assert.Single(higher.Contexts);
    }

    [Fact]
    public void Create_ReturnsNullForUnsupportedContainer()
    {
        var backend = new FakeBackend("fake", 0, PlaybackMediaContainer.FlashVideo);
        var factory = new PlaybackDecoderBackendFactory([backend]);

        Assert.Null(factory.Create(CreateContext(PlaybackMediaContainer.MpegTransportStream)));
        Assert.Empty(backend.Contexts);
    }

    [Fact]
    public void DuplicateIds_AreRejected()
    {
        var exception = Assert.Throws<PlaybackDecoderBackendException>(() =>
            new PlaybackDecoderBackendFactory([
                new FakeBackend("same", 0, PlaybackMediaContainer.FlashVideo),
                new FakeBackend("same", 1, PlaybackMediaContainer.FlashVideo),
            ]));

        Assert.Equal(PlaybackDecoderBackendFailure.InvalidDescriptor, exception.Failure);
    }

    [Fact]
    public void InitializationFailure_IsMappedWithoutFallback()
    {
        var failing = new FakeBackend("failing", 2, PlaybackMediaContainer.FlashVideo)
        {
            Failure = new InvalidOperationException("backend-specific detail"),
        };
        var fallback = new FakeBackend("fallback", 1, PlaybackMediaContainer.FlashVideo);
        var factory = new PlaybackDecoderBackendFactory([fallback, failing]);

        var exception = Assert.Throws<PlaybackDecoderBackendException>(() =>
            factory.Create(CreateContext(PlaybackMediaContainer.FlashVideo)));

        Assert.Equal(PlaybackDecoderBackendFailure.InitializationFailed, exception.Failure);
        Assert.Equal("failing", exception.BackendId);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("backend-specific detail", exception.Message);
        Assert.Empty(fallback.Contexts);
    }

    [Fact]
    public void DescriptorCapabilities_CannotBeMutatedByCaller()
    {
        var source = new[] { PlaybackMediaContainer.FlashVideo };
        var descriptor = new PlaybackDecoderBackendDescriptor(
            "fake",
            0,
            source,
            PlaybackDecoderBackendProvenance.FirstPartyManaged);

        source[0] = PlaybackMediaContainer.WebM;

        Assert.True(descriptor.Supports(PlaybackMediaContainer.FlashVideo));
        Assert.False(descriptor.Supports(PlaybackMediaContainer.WebM));
        Assert.IsAssignableFrom<IReadOnlyList<PlaybackMediaContainer>>(
            descriptor.SupportedContainers);
    }

    [Theory]
    [InlineData(PlaybackDecoderBackendLicenseReview.Pending)]
    [InlineData(PlaybackDecoderBackendLicenseReview.Rejected)]
    public void UnapprovedLicenseReview_IsAlwaysRejected(
        PlaybackDecoderBackendLicenseReview review)
    {
        var backend = new ProvenanceBackend(new(
            PlaybackDecoderBackendOrigin.ThirdParty,
            PlaybackDecoderBackendDistribution.OptionalExternal,
            review,
            ContainsNativeCode: true));
        var policy = new PlaybackDecoderBackendPolicy
        {
            AllowThirdParty = true,
            AllowNativeCode = true,
        };

        var exception = Assert.Throws<PlaybackDecoderBackendException>(() =>
            new PlaybackDecoderBackendFactory([backend], policy));

        Assert.Equal(PlaybackDecoderBackendFailure.ProvenanceRejected, exception.Failure);
    }

    [Fact]
    public void ApprovedExternalNativeBackend_RequiresExplicitPolicy()
    {
        var backend = new ProvenanceBackend(new(
            PlaybackDecoderBackendOrigin.ThirdParty,
            PlaybackDecoderBackendDistribution.OptionalExternal,
            PlaybackDecoderBackendLicenseReview.Approved,
            ContainsNativeCode: true));

        Assert.Throws<PlaybackDecoderBackendException>(() =>
            new PlaybackDecoderBackendFactory([backend]));
        var factory = new PlaybackDecoderBackendFactory(
            [backend],
            new PlaybackDecoderBackendPolicy
            {
                AllowThirdParty = true,
                AllowNativeCode = true,
            });

        Assert.Single(factory.Backends);
        Assert.Equal(0, backend.CreateCount);
    }

    [Fact]
    public void Audit_ReportsAllFixedRejectionReasonsWithoutInitialization()
    {
        var backend = new ProvenanceBackend(new(
            PlaybackDecoderBackendOrigin.ThirdParty,
            PlaybackDecoderBackendDistribution.Bundled,
            PlaybackDecoderBackendLicenseReview.Pending,
            ContainsNativeCode: true));

        var audit = PlaybackDecoderBackendFactory.AuditBackends([backend]);

        var entry = Assert.Single(audit);
        Assert.Equal("third-party", entry.BackendId);
        Assert.Equal(
            PlaybackDecoderBackendPolicyRejection.LicenseNotApproved |
            PlaybackDecoderBackendPolicyRejection.ThirdPartyNotAllowed |
            PlaybackDecoderBackendPolicyRejection.BundledNotAllowed |
            PlaybackDecoderBackendPolicyRejection.NativeCodeNotAllowed,
            entry.Rejection);
        Assert.False(entry.IsAllowed);
        Assert.Equal(0, backend.CreateCount);
    }

    [Fact]
    public void Audit_IsDeterministicAndAllowedEntryContainsNoRejection()
    {
        var second = new FakeBackend("second", 0, PlaybackMediaContainer.FlashVideo);
        var first = new FakeBackend("first", 0, PlaybackMediaContainer.FlashVideo);

        var audit = PlaybackDecoderBackendFactory.AuditBackends([second, first]);

        Assert.Equal(["first", "second"], audit.Select(static entry => entry.BackendId));
        Assert.All(audit, static entry =>
        {
            Assert.True(entry.IsAllowed);
            Assert.Equal(PlaybackDecoderBackendPolicyRejection.None, entry.Rejection);
        });
        Assert.Empty(first.Contexts);
        Assert.Empty(second.Contexts);
    }

    [Fact]
    public void ThirdPartyDescriptor_RequiresArtifactManifest()
    {
        var provenance = new PlaybackDecoderBackendProvenance(
            PlaybackDecoderBackendOrigin.ThirdParty,
            PlaybackDecoderBackendDistribution.OptionalExternal,
            PlaybackDecoderBackendLicenseReview.Approved,
            ContainsNativeCode: false);

        Assert.Throws<ArgumentException>(() =>
            new PlaybackDecoderBackendDescriptor(
                "third-party",
                0,
                [PlaybackMediaContainer.FlashVideo],
                provenance));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void UndefinedProvenanceEnumsAreRejectedBeforePolicyOrManifestDecision(int field)
    {
        var provenance = new PlaybackDecoderBackendProvenance(
            field == 0 ? (PlaybackDecoderBackendOrigin)7144 : PlaybackDecoderBackendOrigin.FirstParty,
            field == 1 ? (PlaybackDecoderBackendDistribution)7144 : PlaybackDecoderBackendDistribution.BuiltIn,
            field == 2 ? (PlaybackDecoderBackendLicenseReview)7144 : PlaybackDecoderBackendLicenseReview.Approved,
            ContainsNativeCode: false);
        var policy = new PlaybackDecoderBackendPolicy
        {
            AllowThirdParty = true,
            AllowBundled = true,
            AllowNativeCode = true,
        };

        var rejection = policy.Evaluate(provenance);
        var exception = Assert.Throws<ArgumentException>(() =>
            new PlaybackDecoderBackendDescriptor(
                "invalid-provenance",
                0,
                [PlaybackMediaContainer.FlashVideo],
                provenance));

        Assert.Equal(PlaybackDecoderBackendPolicyRejection.InvalidProvenance, rejection);
        Assert.DoesNotContain("7144", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("7144", rejection.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UndefinedSupportedContainerIsRejectedWithoutEchoingValue()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new PlaybackDecoderBackendDescriptor(
                "invalid-container",
                0,
                [(PlaybackMediaContainer)7144],
                PlaybackDecoderBackendProvenance.FirstPartyManaged));

        Assert.DoesNotContain("7144", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ArtifactManifest_NormalizesAndExposesFixedIdentity()
    {
        var manifest = new PlaybackDecoderBackendArtifactManifest(
            "example.decoder",
            "1.2.3",
            "LGPL-2.1-or-later",
            new string('a', 64));

        Assert.Equal("example.decoder", manifest.UpstreamId);
        Assert.Equal("1.2.3", manifest.Version);
        Assert.Equal("LGPL-2.1-or-later", manifest.LicenseId);
        Assert.Equal(new string('A', 64), manifest.Sha256);
    }

    [Theory]
    [InlineData("bad/path", "1.0", "MIT")]
    [InlineData("good", "bad version", "MIT")]
    [InlineData("good", "1.0", "bad/license")]
    public void ArtifactManifest_RejectsUnsafeTokens(
        string upstreamId,
        string version,
        string licenseId)
    {
        Assert.Throws<ArgumentException>(() =>
            new PlaybackDecoderBackendArtifactManifest(
                upstreamId,
                version,
                licenseId,
                new string('0', 64)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("contains/path")]
    public void Descriptor_RejectsUnsafeIds(string id)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new PlaybackDecoderBackendDescriptor(
                id,
                0,
                [PlaybackMediaContainer.FlashVideo],
                PlaybackDecoderBackendProvenance.FirstPartyManaged));
    }

    private static PlaybackDecoderCreationContext CreateContext(
        PlaybackMediaContainer container)
    {
        var constructor = typeof(PlaybackDecoderCreationContext)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();
        return (PlaybackDecoderCreationContext)constructor.Invoke([
            container,
            PlaybackMediaClassificationBasis.FileHeader,
            NullPlaybackDecodedFrameSink.Instance,
        ]);
    }

    private sealed class FakeBackend(
        string id,
        int priority,
        params PlaybackMediaContainer[] containers) : IPlaybackDecoderBackend
    {
        public PlaybackDecoderBackendDescriptor Descriptor { get; } =
            new(
                id,
                priority,
                containers,
                PlaybackDecoderBackendProvenance.FirstPartyManaged);

        public List<PlaybackDecoderCreationContext> Contexts { get; } = [];

        public Exception? Failure { get; init; }

        public IPlaybackDecoder Create(PlaybackDecoderCreationContext context)
        {
            Contexts.Add(context);
            if (Failure is not null)
            {
                throw Failure;
            }

            return new FakeDecoder();
        }
    }

    private sealed class FakeDecoder : IPlaybackDecoder
    {
        public ValueTask DecodeAsync(
            PlaybackEncodedInput input,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask ResetAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask CompleteAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ProvenanceBackend(
        PlaybackDecoderBackendProvenance provenance) : IPlaybackDecoderBackend
    {
        public PlaybackDecoderBackendDescriptor Descriptor { get; } = new(
            "third-party",
            0,
            [PlaybackMediaContainer.FlashVideo],
            provenance,
            new PlaybackDecoderBackendArtifactManifest(
                "example.decoder",
                "1.0.0",
                "MIT",
                new string('0', 64)));

        public int CreateCount { get; private set; }

        public IPlaybackDecoder Create(PlaybackDecoderCreationContext context)
        {
            CreateCount++;
            return new FakeDecoder();
        }
    }
}
