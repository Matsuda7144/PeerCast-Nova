using static PeerCast.Nova.Playback.Tests.PlaybackSourceBoundOneFrameDecodeTestSupport;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackProductionDecoderGenerationTraceOptInTests
{
    [Fact]
    public void Delivery_observation_capture_is_disabled_by_default()
    {
        Assert.False(
            new PlaybackProductionDecoderGenerationFactoryOptions()
                .CaptureDeliveryObservations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Factory_clones_and_forwards_delivery_observation_capture(bool capture)
    {
        using var source = CreateSource();
        using var output = new OutputHarness();
        var operations = new CapturingOperations();
        await using var factory = new PlaybackProductionDecoderGenerationFactory(
            new PlaybackDecoderBackendPolicy
            {
                AllowThirdParty = true,
                AllowNativeCode = true,
            },
            new PlaybackProductionDecoderGenerationFactoryOptions
            {
                CaptureDeliveryObservations = capture,
            },
            operations);

        var creation = await factory.CreateAsync(
            "C:\\verified-runtime",
            source,
            output.Output,
            decoderGeneration: 7,
            discontinuityGeneration: 0);

        Assert.Equal(
            PlaybackProductionDecoderGenerationFactoryResult.Created,
            creation.Result);
        Assert.Equal(capture, operations.CapturedDeliveryObservationSetting);
        var generation = Assert.IsType<PlaybackProductionDecoderGeneration>(
            creation.Generation);

        await generation.DisposeAsync();

        Assert.True(operations.Root!.IsDisposed);
        Assert.False(source.IsDisposed);
    }

    private sealed class OutputHarness : IDisposable
    {
        public OutputHarness()
        {
            Activation = new PlaybackWindowsOutputActivationBoundary();
            Binding = new PlaybackWindowsOutputResourceBindingBoundary(Activation);
            Initialization = new PlaybackWindowsOutputStreamInitializationBoundary(
                Activation,
                Binding);
            Output = new PlaybackWindowsOutputBufferProcessingBoundary(
                Activation,
                Binding,
                Initialization);
        }

        public PlaybackWindowsOutputActivationBoundary Activation { get; }

        public PlaybackWindowsOutputResourceBindingBoundary Binding { get; }

        public PlaybackWindowsOutputStreamInitializationBoundary Initialization { get; }

        public PlaybackWindowsOutputBufferProcessingBoundary Output { get; }

        public void Dispose()
        {
            Output.Dispose();
            Initialization.Dispose();
            Binding.Dispose();
            Activation.Dispose();
        }
    }

    private sealed class CapturingOperations :
        IPlaybackProductionDecoderGenerationFactoryOperations
    {
        public PlaybackDecoderBackendProvenance Provenance { get; } = new(
            PlaybackDecoderBackendOrigin.ThirdParty,
            PlaybackDecoderBackendDistribution.OptionalExternal,
            PlaybackDecoderBackendLicenseReview.Approved,
            ContainsNativeCode: true);

        public bool CapturedDeliveryObservationSetting { get; private set; }

        public FakeRoot? Root { get; private set; }

        public PlaybackProductionDecoderGenerationOwnedCreation PrepareBinding(
            PlaybackBoundedDemuxSession sourceSession)
        {
            Assert.False(sourceSession.IsDisposed);
            return Created(new FakeResource());
        }

        public ValueTask<PlaybackProductionDecoderGenerationOwnedCreation> LocateRuntimeAsync(
            string runtimeDirectory,
            CancellationToken cancellationToken)
        {
            Assert.Equal("C:\\verified-runtime", runtimeDirectory);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Created(new FakeResource()));
        }

        public PlaybackProductionDecoderGenerationOwnedCreation AcquireGate(
            IDisposable runtimeOwner) =>
            Created(new FakeResource(Assert.IsType<FakeResource>(runtimeOwner)));

        public PlaybackProductionDecoderGenerationOwnedCreation OpenNativeSession(
            IDisposable bindingOwner,
            IDisposable videoGateOwner,
            IDisposable audioGateOwner) =>
            Created(new FakeResource(
                Assert.IsType<FakeResource>(bindingOwner),
                Assert.IsType<FakeResource>(videoGateOwner),
                Assert.IsType<FakeResource>(audioGateOwner)));

        public PlaybackProductionDecoderGenerationRootCreation CreateGenerationRoot(
            PlaybackBoundedDemuxSession sourceSession,
            IDisposable nativeSessionOwner,
            int decoderGeneration,
            int discontinuityGeneration,
            PlaybackWindowsOutputBufferProcessingBoundary output,
            PlaybackProductionDecoderGenerationFactoryOptions options)
        {
            Assert.NotEqual(PlaybackWindowsOutputBufferState.Disposed, output.Snapshot.VideoState);
            Assert.NotEqual(PlaybackWindowsOutputBufferState.Disposed, output.Snapshot.AudioState);
            CapturedDeliveryObservationSetting = options.CaptureDeliveryObservations;
            Root = new(
                sourceSession,
                decoderGeneration,
                discontinuityGeneration,
                Assert.IsType<FakeResource>(nativeSessionOwner));
            return new(PlaybackProductionDecoderGenerationStageResult.Created, Root);
        }

        private static PlaybackProductionDecoderGenerationOwnedCreation Created(
            IDisposable owner) =>
            new(PlaybackProductionDecoderGenerationStageResult.Created, owner);
    }

    private sealed class FakeResource(params IDisposable[] owned) : IDisposable
    {
        private bool _isDisposed;

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            for (var index = owned.Length - 1; index >= 0; index--)
            {
                owned[index].Dispose();
            }
        }
    }

    private sealed class FakeRoot : IPlaybackProductionDecoderGenerationRoot
    {
        private readonly FakeResource _native;

        public FakeRoot(
            PlaybackBoundedDemuxSession sourceSession,
            int decoderGeneration,
            int discontinuityGeneration,
            FakeResource native)
        {
            SourceSession = sourceSession;
            DecoderGeneration = decoderGeneration;
            DiscontinuityGeneration = discontinuityGeneration;
            _native = native;
        }

        public PlaybackBoundedDemuxSession SourceSession { get; }

        public int DecoderGeneration { get; }

        public int DiscontinuityGeneration { get; }

        public bool IsDisposed { get; private set; }

        public PlaybackGenerationMatchedSchedulerDeliverySnapshot Snapshot =>
            throw new NotSupportedException();

        public bool ReferencesSourceSession(PlaybackBoundedDemuxSession? sourceSession) =>
            ReferenceEquals(SourceSession, sourceSession);

        public PlaybackDecodedCompletionSchedulerIngressResult Transfer(
            PlaybackSourceBoundDecodedFrameCompletion? completion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            completion?.Dispose();
            return IsDisposed
                ? PlaybackDecodedCompletionSchedulerIngressResult.Disposed
                : PlaybackDecodedCompletionSchedulerIngressResult.Transferred;
        }

        public ValueTask<PlaybackGenerationMatchedSchedulerDeliveryResult> DeliverOneAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(IsDisposed
                ? PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed
                : PlaybackGenerationMatchedSchedulerDeliveryResult.Empty);
        }

        public ValueTask<PlaybackGenerationMatchedSchedulerResetResult> ResetAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(IsDisposed
                ? PlaybackGenerationMatchedSchedulerResetResult.Disposed
                : PlaybackGenerationMatchedSchedulerResetResult.Reset);
        }

        public ValueTask StopAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            _native.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
