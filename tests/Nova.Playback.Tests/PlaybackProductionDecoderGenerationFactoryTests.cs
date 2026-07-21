using static PeerCast.Nova.Playback.Tests.PlaybackSourceBoundOneFrameDecodeTestSupport;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackProductionDecoderGenerationFactoryTests
{
    [Fact]
    public async Task Pending_license_review_stops_before_binding_or_runtime_observation()
    {
        using var source = CreateSource();
        using var output = new OutputHarness();
        var operations = new FakeOperations
        {
            Provenance = Provenance(PlaybackDecoderBackendLicenseReview.Pending),
        };
        await using var factory = CreateFactory(operations);

        var creation = await factory.CreateAsync(
            "Z:\\must-not-be-observed",
            source,
            output.Output,
            decoderGeneration: 3,
            discontinuityGeneration: 0);

        Assert.Equal(
            PlaybackProductionDecoderGenerationFactoryResult.LicenseReviewPending,
            creation.Result);
        Assert.Null(creation.Generation);
        Assert.Empty(operations.Calls);
        Assert.Equal(0, factory.Snapshot.CreatedGenerationCount);
    }

    [Fact]
    public async Task Rejected_policy_stops_before_binding_or_runtime_observation()
    {
        using var source = CreateSource();
        using var output = new OutputHarness();
        var operations = new FakeOperations();
        await using var factory = new PlaybackProductionDecoderGenerationFactory(
            new PlaybackDecoderBackendPolicy
            {
                AllowThirdParty = false,
                AllowNativeCode = true,
            },
            null,
            operations);

        var creation = await factory.CreateAsync(
            "Z:\\must-not-be-observed",
            source,
            output.Output,
            3,
            0);

        Assert.Equal(
            PlaybackProductionDecoderGenerationFactoryResult.PolicyRejected,
            creation.Result);
        Assert.Null(creation.Generation);
        Assert.Empty(operations.Calls);
    }

    [Fact]
    public async Task Approved_creation_builds_two_independent_chains_and_one_complete_root()
    {
        using var source = CreateSource();
        using var output = new OutputHarness();
        var operations = new FakeOperations();
        await using var factory = CreateFactory(operations);

        var creation = await factory.CreateAsync(
            "C:\\verified-runtime",
            source,
            output.Output,
            decoderGeneration: 7,
            discontinuityGeneration: 2);

        Assert.Equal(PlaybackProductionDecoderGenerationFactoryResult.Created, creation.Result);
        var generation = Assert.IsType<PlaybackProductionDecoderGeneration>(creation.Generation);
        Assert.Same(source, generation.SourceSession);
        Assert.Equal(7, generation.DecoderGeneration);
        Assert.Equal(2, generation.DiscontinuityGeneration);
        Assert.Equal(
            [
                "binding",
                "runtime:video",
                "gate:video-runtime",
                "runtime:audio",
                "gate:audio-runtime",
                "native",
                "root",
            ],
            operations.Calls);
        Assert.NotSame(operations.VideoRuntime, operations.AudioRuntime);
        Assert.NotSame(operations.VideoGate, operations.AudioGate);
        Assert.Equal(1, factory.Snapshot.CreatedGenerationCount);

        await generation.DisposeAsync();

        Assert.True(operations.Root!.IsDisposed);
        Assert.False(source.IsDisposed);
        Assert.NotEqual(
            PlaybackWindowsOutputBufferState.Disposed,
            output.Output.Snapshot.VideoState);
        Assert.NotEqual(
            PlaybackWindowsOutputBufferState.Disposed,
            output.Output.Snapshot.AudioState);
    }

    [Fact]
    public async Task Shared_runtime_is_rejected_before_audio_gate_or_native_creation()
    {
        using var source = CreateSource();
        using var output = new OutputHarness();
        var operations = new FakeOperations { ShareRuntime = true };
        await using var factory = CreateFactory(operations);

        var creation = await factory.CreateAsync(
            "C:\\verified-runtime",
            source,
            output.Output,
            1,
            0);

        Assert.Equal(
            PlaybackProductionDecoderGenerationFactoryResult.OwnershipConflict,
            creation.Result);
        Assert.Equal(
            ["binding", "runtime:video", "gate:video-runtime", "runtime:audio"],
            operations.Calls);
        Assert.Equal(1, operations.VideoRuntime!.DisposeCount);
        Assert.Equal(1, operations.VideoGate!.DisposeCount);
        Assert.Equal(1, operations.Binding!.DisposeCount);
        Assert.Null(operations.Native);
    }

    [Fact]
    public async Task Shared_gate_is_rejected_and_every_runtime_is_released_once()
    {
        using var source = CreateSource();
        using var output = new OutputHarness();
        var operations = new FakeOperations { ShareGate = true };
        await using var factory = CreateFactory(operations);

        var creation = await factory.CreateAsync(
            "C:\\verified-runtime",
            source,
            output.Output,
            1,
            0);

        Assert.Equal(
            PlaybackProductionDecoderGenerationFactoryResult.OwnershipConflict,
            creation.Result);
        Assert.DoesNotContain("native", operations.Calls);
        Assert.Equal(1, operations.VideoGate!.DisposeCount);
        Assert.Equal(1, operations.VideoRuntime!.DisposeCount);
        Assert.Equal(1, operations.AudioRuntime!.DisposeCount);
        Assert.Equal(1, operations.Binding!.DisposeCount);
    }

    [Fact]
    public async Task Video_runtime_rejection_releases_only_owned_binding()
    {
        using var source = CreateSource();
        using var output = new OutputHarness();
        var operations = new FakeOperations
        {
            VideoRuntimeResult = PlaybackProductionDecoderGenerationStageResult.Rejected,
        };
        await using var factory = CreateFactory(operations);

        var creation = await factory.CreateAsync(
            "C:\\verified-runtime",
            source,
            output.Output,
            1,
            0);

        Assert.Equal(
            PlaybackProductionDecoderGenerationFactoryResult.VideoRuntimeRejected,
            creation.Result);
        Assert.Equal(["binding", "runtime:video"], operations.Calls);
        Assert.Equal(1, operations.Binding!.DisposeCount);
        Assert.Null(operations.VideoRuntime);
    }

    [Fact]
    public async Task Native_rejection_consumes_binding_and_gates_then_releases_runtime_chains()
    {
        using var source = CreateSource();
        using var output = new OutputHarness();
        var operations = new FakeOperations
        {
            NativeResult = PlaybackProductionDecoderGenerationStageResult.Rejected,
        };
        await using var factory = CreateFactory(operations);

        var creation = await factory.CreateAsync(
            "C:\\verified-runtime",
            source,
            output.Output,
            1,
            0);

        Assert.Equal(
            PlaybackProductionDecoderGenerationFactoryResult.NativeSessionRejected,
            creation.Result);
        Assert.DoesNotContain("root", operations.Calls);
        Assert.Equal(1, operations.Binding!.DisposeCount);
        Assert.Equal(1, operations.VideoGate!.DisposeCount);
        Assert.Equal(1, operations.AudioGate!.DisposeCount);
        Assert.Equal(1, operations.VideoRuntime!.DisposeCount);
        Assert.Equal(1, operations.AudioRuntime!.DisposeCount);
    }

    [Fact]
    public async Task Root_rejection_disposes_native_chain_and_returns_no_partial_product()
    {
        using var source = CreateSource();
        using var output = new OutputHarness();
        var operations = new FakeOperations
        {
            RootResult = PlaybackProductionDecoderGenerationStageResult.Rejected,
        };
        await using var factory = CreateFactory(operations);

        var creation = await factory.CreateAsync(
            "C:\\verified-runtime",
            source,
            output.Output,
            1,
            0);

        Assert.Equal(
            PlaybackProductionDecoderGenerationFactoryResult.GenerationRejected,
            creation.Result);
        Assert.Null(creation.Generation);
        Assert.Equal(1, operations.Native!.DisposeCount);
        Assert.Equal(1, operations.VideoRuntime!.DisposeCount);
        Assert.Equal(1, operations.AudioRuntime!.DisposeCount);
    }

    [Fact]
    public async Task Pre_cancelled_creation_performs_no_binding_or_runtime_work()
    {
        using var source = CreateSource();
        using var output = new OutputHarness();
        var operations = new FakeOperations();
        await using var factory = CreateFactory(operations);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var creation = await factory.CreateAsync(
            "C:\\verified-runtime",
            source,
            output.Output,
            1,
            0,
            cancellation.Token);

        Assert.Equal(
            PlaybackProductionDecoderGenerationFactoryResult.Cancelled,
            creation.Result);
        Assert.Empty(operations.Calls);
    }

    [Fact]
    public async Task Disposed_output_is_rejected_without_parameter_or_runtime_ownership()
    {
        using var source = CreateSource();
        using var output = new OutputHarness();
        output.Output.Dispose();
        var operations = new FakeOperations();
        await using var factory = CreateFactory(operations);

        var creation = await factory.CreateAsync(
            "C:\\verified-runtime",
            source,
            output.Output,
            1,
            0);

        Assert.Equal(
            PlaybackProductionDecoderGenerationFactoryResult.OutputUnavailable,
            creation.Result);
        Assert.Empty(operations.Calls);
    }

    private static PlaybackProductionDecoderGenerationFactory CreateFactory(
        FakeOperations operations) =>
        new(
            new PlaybackDecoderBackendPolicy
            {
                AllowThirdParty = true,
                AllowNativeCode = true,
            },
            null,
            operations);

    private static PlaybackDecoderBackendProvenance Provenance(
        PlaybackDecoderBackendLicenseReview review) =>
        new(
            PlaybackDecoderBackendOrigin.ThirdParty,
            PlaybackDecoderBackendDistribution.OptionalExternal,
            review,
            ContainsNativeCode: true);

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

    private sealed class FakeOperations : IPlaybackProductionDecoderGenerationFactoryOperations
    {
        private int _runtimeCount;
        private int _gateCount;

        public PlaybackDecoderBackendProvenance Provenance { get; init; } =
            PlaybackProductionDecoderGenerationFactoryTests.Provenance(
                PlaybackDecoderBackendLicenseReview.Approved);

        public PlaybackProductionDecoderGenerationStageResult VideoRuntimeResult { get; init; } =
            PlaybackProductionDecoderGenerationStageResult.Created;

        public PlaybackProductionDecoderGenerationStageResult NativeResult { get; init; } =
            PlaybackProductionDecoderGenerationStageResult.Created;

        public PlaybackProductionDecoderGenerationStageResult RootResult { get; init; } =
            PlaybackProductionDecoderGenerationStageResult.Created;

        public bool ShareRuntime { get; init; }

        public bool ShareGate { get; init; }

        public List<string> Calls { get; } = [];

        public FakeResource? Binding { get; private set; }

        public FakeResource? VideoRuntime { get; private set; }

        public FakeResource? AudioRuntime { get; private set; }

        public FakeResource? VideoGate { get; private set; }

        public FakeResource? AudioGate { get; private set; }

        public FakeResource? Native { get; private set; }

        public FakeRoot? Root { get; private set; }

        public PlaybackProductionDecoderGenerationOwnedCreation PrepareBinding(
            PlaybackBoundedDemuxSession sourceSession)
        {
            Calls.Add("binding");
            Binding = new();
            return new(PlaybackProductionDecoderGenerationStageResult.Created, Binding);
        }

        public ValueTask<PlaybackProductionDecoderGenerationOwnedCreation> LocateRuntimeAsync(
            string runtimeDirectory,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _runtimeCount++;
            var video = _runtimeCount == 1;
            Calls.Add(video ? "runtime:video" : "runtime:audio");
            if (video && VideoRuntimeResult != PlaybackProductionDecoderGenerationStageResult.Created)
            {
                return ValueTask.FromResult(
                    new PlaybackProductionDecoderGenerationOwnedCreation(VideoRuntimeResult, null));
            }

            if (video)
            {
                VideoRuntime = new();
                return ValueTask.FromResult(
                    new PlaybackProductionDecoderGenerationOwnedCreation(
                        PlaybackProductionDecoderGenerationStageResult.Created,
                        VideoRuntime));
            }

            AudioRuntime = ShareRuntime ? VideoRuntime : new FakeResource();
            return ValueTask.FromResult(
                new PlaybackProductionDecoderGenerationOwnedCreation(
                    PlaybackProductionDecoderGenerationStageResult.Created,
                    AudioRuntime));
        }

        public PlaybackProductionDecoderGenerationOwnedCreation AcquireGate(
            IDisposable runtimeOwner)
        {
            _gateCount++;
            var video = _gateCount == 1;
            var runtime = Assert.IsType<FakeResource>(runtimeOwner);
            Calls.Add(video ? "gate:video-runtime" : "gate:audio-runtime");
            if (video)
            {
                VideoGate = new(runtime);
                return new(
                    PlaybackProductionDecoderGenerationStageResult.Created,
                    VideoGate);
            }

            AudioGate = ShareGate ? VideoGate : new FakeResource(runtime);
            return new(
                PlaybackProductionDecoderGenerationStageResult.Created,
                AudioGate);
        }

        public PlaybackProductionDecoderGenerationOwnedCreation OpenNativeSession(
            IDisposable bindingOwner,
            IDisposable videoGateOwner,
            IDisposable audioGateOwner)
        {
            Calls.Add("native");
            var binding = Assert.IsType<FakeResource>(bindingOwner);
            var videoGate = Assert.IsType<FakeResource>(videoGateOwner);
            var audioGate = Assert.IsType<FakeResource>(audioGateOwner);
            binding.Dispose();
            if (NativeResult != PlaybackProductionDecoderGenerationStageResult.Created)
            {
                audioGate.Dispose();
                videoGate.Dispose();
                return new(NativeResult, null);
            }

            Native = new(videoGate, audioGate);
            return new(NativeResult, Native);
        }

        public PlaybackProductionDecoderGenerationRootCreation CreateGenerationRoot(
            PlaybackBoundedDemuxSession sourceSession,
            IDisposable nativeSessionOwner,
            int decoderGeneration,
            int discontinuityGeneration,
            PlaybackWindowsOutputBufferProcessingBoundary output,
            PlaybackProductionDecoderGenerationFactoryOptions options)
        {
            Calls.Add("root");
            var native = Assert.IsType<FakeResource>(nativeSessionOwner);
            if (RootResult != PlaybackProductionDecoderGenerationStageResult.Created)
            {
                native.Dispose();
                return new(RootResult, null);
            }

            Root = new(sourceSession, decoderGeneration, discontinuityGeneration, native);
            return new(RootResult, Root);
        }
    }

    private sealed class FakeResource(params IDisposable[] owned) : IDisposable
    {
        private bool _isDisposed;

        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            DisposeCount++;
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
            CancellationToken cancellationToken) =>
            IsDisposed
                ? PlaybackDecodedCompletionSchedulerIngressResult.Disposed
                : PlaybackDecodedCompletionSchedulerIngressResult.Transferred;

        public ValueTask<PlaybackGenerationMatchedSchedulerDeliveryResult> DeliverOneAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(IsDisposed
                ? PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed
                : PlaybackGenerationMatchedSchedulerDeliveryResult.Empty);

        public ValueTask<PlaybackGenerationMatchedSchedulerResetResult> ResetAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(IsDisposed
                ? PlaybackGenerationMatchedSchedulerResetResult.Disposed
                : PlaybackGenerationMatchedSchedulerResetResult.Reset);

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
