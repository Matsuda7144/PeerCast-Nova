namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffRegularMovieWindowsOutputSinkTests
{
    [Fact]
    public async Task Write_ForwardsCanonicalVideoAndAudioWithExactGenerations()
    {
        using var fixture = await OutputFixture.CreateAsync();
        using var sink = Assert.IsType<
            PlaybackIsoBmffRegularMovieWindowsOutputSink>(
                new PlaybackIsoBmffRegularMovieWindowsOutputSinkFactory()
                    .Create(fixture.Output, 7, 11).Sink);
        var video = PlaybackDecodedVideoFrame.Create(
            3,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(20),
            PlaybackDecodedFrameDiscontinuityKind.None,
            PlaybackDecodedVideoPixelFormat.Bgra32,
            2,
            2,
            8,
            new byte[16]);
        var audio = PlaybackDecodedAudioFrame.Create(
            4,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(20),
            PlaybackDecodedFrameDiscontinuityKind.None,
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
            48_000,
            2,
            2,
            new byte[8]);

        await sink.WriteVideoAsync(video, default);
        await sink.WriteAudioAsync(audio, default);

        Assert.Equal(
            new PlaybackWindowsVideoFrameBuffer(
                PlaybackDecodedVideoPixelFormat.Bgra32,
                2,
                2,
                8,
                video.Payload,
                7),
            Assert.Single(fixture.Processor.VideoFrames));
        Assert.Equal(
            new PlaybackWindowsAudioSampleBuffer(
                PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                48_000,
                2,
                2,
                audio.Payload,
                11),
            Assert.Single(fixture.Processor.AudioBuffers));
        Assert.Equal([true], fixture.Processor.AudioStarts);
        Assert.Equal(1, sink.Snapshot.ProcessedVideoCount);
        Assert.Equal(1, sink.Snapshot.ProcessedAudioCount);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieWindowsOutputSinkResult.AudioProcessed,
            sink.Snapshot.Result);

        sink.Dispose();
        Assert.Equal(
            PlaybackWindowsOutputBufferState.Ready,
            fixture.Output.Snapshot.VideoState);
        Assert.Equal(
            PlaybackWindowsOutputBufferState.Ready,
            fixture.Output.Snapshot.AudioState);
    }

    [Fact]
    public async Task Write_FailsClosedOnOutputRejectionWithoutOwningOutput()
    {
        using var fixture = await OutputFixture.CreateAsync(
            videoResult: PlaybackWindowsOutputNativeBufferResult.WouldBlock);
        using var sink = Assert.IsType<
            PlaybackIsoBmffRegularMovieWindowsOutputSink>(
                new PlaybackIsoBmffRegularMovieWindowsOutputSinkFactory()
                    .Create(fixture.Output, 7, 11).Sink);
        var frame = PlaybackDecodedVideoFrame.Create(
            0,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(20),
            PlaybackDecodedFrameDiscontinuityKind.None,
            PlaybackDecodedVideoPixelFormat.Bgra32,
            2,
            2,
            8,
            new byte[16]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sink.WriteVideoAsync(frame, default).AsTask());

        Assert.Equal(
            PlaybackIsoBmffRegularMovieWindowsOutputSinkState.Faulted,
            sink.Snapshot.State);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieWindowsOutputSinkResult.OutputRejected,
            sink.Snapshot.Result);
        Assert.Equal(0, sink.Snapshot.ProcessedVideoCount);
        sink.Dispose();
        Assert.Equal(
            PlaybackWindowsOutputBufferState.Ready,
            fixture.Output.Snapshot.VideoState);
    }

    [Fact]
    public async Task Reconfigure_UpdatesAllOutputLayersBeforeNextWrite()
    {
        using var fixture = await OutputFixture.CreateAsync();
        using var reconfiguration = Assert.IsType<
            PlaybackIsoBmffRegularMovieWindowsOutputReconfiguration>(
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationFactory
                    .Create(
                        fixture.Binding,
                        fixture.Initialization,
                        fixture.Output,
                        OutputConfiguration()).Reconfiguration);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                .Reconfigured,
            await reconfiguration.ReconfigureAsync(13, 17));

        Assert.Equal(13, fixture.Binding.Snapshot.VideoGeneration);
        Assert.Equal(17, fixture.Binding.Snapshot.AudioGeneration);
        Assert.Equal(13, fixture.Initialization.Snapshot.VideoGeneration);
        Assert.Equal(17, fixture.Initialization.Snapshot.AudioGeneration);
        Assert.Equal(13, fixture.Output.Snapshot.VideoGeneration);
        Assert.Equal(17, fixture.Output.Snapshot.AudioGeneration);
        Assert.Equal(
            PlaybackWindowsOutputBindingState.Bound,
            fixture.Binding.Snapshot.VideoState);
        Assert.Equal(
            PlaybackWindowsOutputStreamState.Initialized,
            fixture.Initialization.Snapshot.AudioState);
        Assert.Equal(1, reconfiguration.Snapshot.ReconfigurationCount);
        Assert.Equal(
            13,
            reconfiguration.Snapshot.Configuration.VideoBinding.Generation);
        Assert.Equal(
            17,
            reconfiguration.Snapshot.Configuration.AudioStream.Generation);

        using var sink = Assert.IsType<
            PlaybackIsoBmffRegularMovieWindowsOutputSink>(
                new PlaybackIsoBmffRegularMovieWindowsOutputSinkFactory()
                    .Create(fixture.Output, 13, 17).Sink);
        await sink.WriteVideoAsync(
            PlaybackDecodedVideoFrame.Create(
                0,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(20),
                PlaybackDecodedFrameDiscontinuityKind.None,
                PlaybackDecodedVideoPixelFormat.Bgra32,
                2,
                2,
                8,
                new byte[16]),
            default);
        Assert.Equal(13, Assert.Single(fixture.Processor.VideoFrames).Generation);
    }

    [Fact]
    public async Task Reconfigure_RollsBackExactPreviousConfigurationOnRejection()
    {
        var operations = new FakeReconfigurationOperations(7);
        using var reconfiguration =
            new PlaybackIsoBmffRegularMovieWindowsOutputReconfiguration(
                OutputConfiguration(),
                operations);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                .ReconfigurationRejected,
            await reconfiguration.ReconfigureAsync(13, 17));

        Assert.Equal(14, operations.Calls.Count);
        Assert.Equal("buffer-audio:17", operations.Calls[0]);
        Assert.Equal("initialize:13:17", operations.Calls[6]);
        Assert.Equal("buffer-audio:11", operations.Calls[7]);
        Assert.Equal("initialize:7:11", operations.Calls[13]);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationState.Ready,
            reconfiguration.Snapshot.State);
        Assert.Equal(7, reconfiguration.Snapshot.Configuration.VideoStream.Generation);
        Assert.Equal(11, reconfiguration.Snapshot.Configuration.AudioBinding.Generation);
        Assert.Equal(0, reconfiguration.Snapshot.ReconfigurationCount);
    }

    [Fact]
    public async Task Reconfigure_FaultsWhenPreviousConfigurationCannotBeRestored()
    {
        var operations = new FakeReconfigurationOperations(7, 8);
        using var reconfiguration =
            new PlaybackIsoBmffRegularMovieWindowsOutputReconfiguration(
                OutputConfiguration(),
                operations);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                .RollbackFaulted,
            await reconfiguration.ReconfigureAsync(13, 17));

        Assert.Equal(
            PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationState.Faulted,
            reconfiguration.Snapshot.State);
        Assert.Equal(8, operations.Calls.Count);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                .RollbackFaulted,
            await reconfiguration.ReconfigureAsync(19, 23));
    }

    private static PlaybackIsoBmffRegularMovieWindowsOutputConfiguration
        OutputConfiguration() =>
        new(
            new(
                PlaybackDecodedVideoPixelFormat.Bgra32,
                2,
                2,
                8,
                7),
            new(
                PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                48_000,
                2,
                11),
            new(
                1,
                2,
                2,
                PlaybackDecodedVideoPixelFormat.Bgra32,
                7),
            new(
                PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                48_000,
                2,
                TimeSpan.FromMilliseconds(100),
                11));

    private sealed class OutputFixture : IDisposable
    {
        private OutputFixture(
            PlaybackWindowsOutputActivationBoundary activation,
            PlaybackWindowsOutputResourceBindingBoundary binding,
            PlaybackWindowsOutputStreamInitializationBoundary initialization,
            PlaybackWindowsOutputBufferProcessingBoundary output,
            RecordingProcessor processor)
        {
            Activation = activation;
            Binding = binding;
            Initialization = initialization;
            Output = output;
            Processor = processor;
        }

        public PlaybackWindowsOutputActivationBoundary Activation { get; }

        public PlaybackWindowsOutputResourceBindingBoundary Binding { get; }

        public PlaybackWindowsOutputStreamInitializationBoundary Initialization
        { get; }

        public PlaybackWindowsOutputBufferProcessingBoundary Output { get; }

        public RecordingProcessor Processor { get; }

        public static async Task<OutputFixture> CreateAsync(
            PlaybackWindowsOutputNativeBufferResult videoResult =
                PlaybackWindowsOutputNativeBufferResult.Processed)
        {
            var activation = new PlaybackWindowsOutputActivationBoundary(
                new RecordingActivation(),
                static () => true);
            Assert.Equal(
                PlaybackWindowsOutputActivationResult.Activated,
                activation.Activate());
            var binding = new PlaybackWindowsOutputResourceBindingBoundary(
                activation,
                new RecordingBinding(),
                static () => true);
            Assert.Equal(
                PlaybackWindowsOutputBindingResult.Bound,
                await binding.ConfigureAsync(
                    new(
                        PlaybackDecodedVideoPixelFormat.Bgra32,
                        2,
                        2,
                        8,
                        7),
                    new(
                        PlaybackDecodedAudioSampleFormat
                            .PcmSigned16Interleaved,
                        48_000,
                        2,
                        11),
                    default));
            var initialization =
                new PlaybackWindowsOutputStreamInitializationBoundary(
                    activation,
                    binding,
                    new RecordingInitializer(),
                    static () => true);
            Assert.Equal(
                PlaybackWindowsOutputStreamInitializationResult.Initialized,
                await initialization.InitializeAsync(
                    new(
                        1,
                        2,
                        2,
                        PlaybackDecodedVideoPixelFormat.Bgra32,
                        7),
                    new(
                        PlaybackDecodedAudioSampleFormat
                            .PcmSigned16Interleaved,
                        48_000,
                        2,
                        TimeSpan.FromMilliseconds(100),
                        11),
                    default));
            var processor = new RecordingProcessor(videoResult);
            var output = new PlaybackWindowsOutputBufferProcessingBoundary(
                activation,
                binding,
                initialization,
                processor,
                static () => true);
            return new(
                activation,
                binding,
                initialization,
                output,
                processor);
        }

        public void Dispose()
        {
            Output.Dispose();
            Initialization.Dispose();
            Binding.Dispose();
            Activation.Dispose();
        }
    }

    private sealed class RecordingActivation :
        IPlaybackWindowsOutputNativeActivation
    {
        public PlaybackWindowsOutputActivationResult InitializeComApartment(
            out IDisposable? apartment)
        {
            apartment = new Lease();
            return PlaybackWindowsOutputActivationResult.Activated;
        }

        public PlaybackWindowsOutputActivationResult ActivateDirect3D11(
            out IDisposable? device)
        {
            device = new Lease();
            return PlaybackWindowsOutputActivationResult.Activated;
        }

        public PlaybackWindowsOutputActivationResult
            ActivateWasapiSharedEventDriven(out IDisposable? audio)
        {
            audio = new Lease();
            return PlaybackWindowsOutputActivationResult.Activated;
        }
    }

    private sealed class FakeReconfigurationOperations(
        params int[] failureCalls) :
        IPlaybackIsoBmffRegularMovieWindowsOutputReconfigurationOperations
    {
        private readonly HashSet<int> _failureCalls = new(failureCalls);

        public List<string> Calls { get; } = [];

        public ValueTask<PlaybackWindowsOutputBufferResetResult>
            ResetAudioBufferAsync(
                long generation,
                CancellationToken cancellationToken)
        {
            Calls.Add($"buffer-audio:{generation}");
            return ValueTask.FromResult(
                Failed()
                    ? PlaybackWindowsOutputBufferResetResult.Failure
                    : PlaybackWindowsOutputBufferResetResult.Reset);
        }

        public ValueTask<PlaybackWindowsOutputBufferResetResult>
            ResetVideoBufferAsync(
                long generation,
                CancellationToken cancellationToken)
        {
            Calls.Add($"buffer-video:{generation}");
            return ValueTask.FromResult(
                Failed()
                    ? PlaybackWindowsOutputBufferResetResult.Failure
                    : PlaybackWindowsOutputBufferResetResult.Reset);
        }

        public ValueTask<PlaybackWindowsOutputStreamResetResult>
            ResetAudioStreamAsync(
                long generation,
                CancellationToken cancellationToken)
        {
            Calls.Add($"stream-audio:{generation}");
            return ValueTask.FromResult(
                Failed()
                    ? PlaybackWindowsOutputStreamResetResult.Failure
                    : PlaybackWindowsOutputStreamResetResult.Reset);
        }

        public ValueTask<PlaybackWindowsOutputStreamResetResult>
            ResetVideoStreamAsync(
                long generation,
                CancellationToken cancellationToken)
        {
            Calls.Add($"stream-video:{generation}");
            return ValueTask.FromResult(
                Failed()
                    ? PlaybackWindowsOutputStreamResetResult.Failure
                    : PlaybackWindowsOutputStreamResetResult.Reset);
        }

        public ValueTask<PlaybackWindowsOutputBindingResetResult>
            ResetBindingAsync(
                long generation,
                CancellationToken cancellationToken)
        {
            Calls.Add($"binding-reset:{generation}");
            return ValueTask.FromResult(
                Failed()
                    ? PlaybackWindowsOutputBindingResetResult.Failure
                    : PlaybackWindowsOutputBindingResetResult.Reset);
        }

        public ValueTask<PlaybackWindowsOutputBindingResult>
            ConfigureBindingAsync(
                PlaybackVideoRendererConfiguration video,
                PlaybackAudioDeviceConfiguration audio,
                CancellationToken cancellationToken)
        {
            Calls.Add($"binding:{video.Generation}:{audio.Generation}");
            return ValueTask.FromResult(
                Failed()
                    ? PlaybackWindowsOutputBindingResult.Failure
                    : PlaybackWindowsOutputBindingResult.Bound);
        }

        public ValueTask<PlaybackWindowsOutputStreamInitializationResult>
            InitializeStreamsAsync(
                PlaybackWindowsVideoStreamConfiguration video,
                PlaybackWindowsAudioStreamConfiguration audio,
                CancellationToken cancellationToken)
        {
            Calls.Add($"initialize:{video.Generation}:{audio.Generation}");
            return ValueTask.FromResult(
                Failed()
                    ? PlaybackWindowsOutputStreamInitializationResult.Failure
                    : PlaybackWindowsOutputStreamInitializationResult
                        .Initialized);
        }

        private bool Failed() => _failureCalls.Contains(Calls.Count);
    }

    private sealed class RecordingBinding :
        IPlaybackWindowsOutputNativeResourceBinding
    {
        public PlaybackWindowsOutputNativeBindingResult BindVideo(
            IDisposable resource,
            PlaybackVideoRendererConfiguration configuration) =>
            PlaybackWindowsOutputNativeBindingResult.Bound;

        public PlaybackWindowsOutputNativeBindingResult BindAudio(
            IDisposable resource,
            PlaybackAudioDeviceConfiguration configuration) =>
            PlaybackWindowsOutputNativeBindingResult.Bound;

        public void UnbindVideo()
        {
        }

        public void UnbindAudio()
        {
        }
    }

    private sealed class RecordingInitializer :
        IPlaybackWindowsOutputNativeStreamInitializer
    {
        public PlaybackWindowsOutputNativeStreamResult InitializeVideo(
            IDisposable resource,
            PlaybackWindowsVideoStreamConfiguration configuration) =>
            PlaybackWindowsOutputNativeStreamResult.Initialized;

        public PlaybackWindowsOutputNativeStreamResult ResizeVideo(
            IDisposable resource,
            PlaybackWindowsVideoStreamConfiguration configuration) =>
            PlaybackWindowsOutputNativeStreamResult.Initialized;

        public PlaybackWindowsOutputNativeStreamResult InitializeAudio(
            IDisposable resource,
            PlaybackWindowsAudioStreamConfiguration configuration) =>
            PlaybackWindowsOutputNativeStreamResult.Initialized;

        public void ResetVideo(IDisposable resource)
        {
        }

        public void ResetAudio(IDisposable resource)
        {
        }
    }

    private sealed class RecordingProcessor(
        PlaybackWindowsOutputNativeBufferResult videoResult) :
        IPlaybackWindowsOutputNativeBufferProcessor
    {
        public List<PlaybackWindowsVideoFrameBuffer> VideoFrames { get; } = [];

        public List<PlaybackWindowsAudioSampleBuffer> AudioBuffers { get; } = [];

        public List<bool> AudioStarts { get; } = [];

        public PlaybackWindowsOutputNativeBufferResult ProcessVideo(
            IDisposable resource,
            PlaybackWindowsVideoFrameBuffer frame)
        {
            VideoFrames.Add(frame);
            return videoResult;
        }

        public PlaybackWindowsOutputNativeBufferResult ProcessAudio(
            IDisposable resource,
            PlaybackWindowsAudioSampleBuffer samples,
            bool startStream)
        {
            AudioBuffers.Add(samples);
            AudioStarts.Add(startStream);
            return PlaybackWindowsOutputNativeBufferResult.Processed;
        }

        public void ResetVideo(IDisposable resource)
        {
        }

        public void ResetAudio(IDisposable resource)
        {
        }
    }

    private sealed class Lease : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
