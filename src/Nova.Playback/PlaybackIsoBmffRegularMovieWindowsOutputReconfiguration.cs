namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationCreationResult
{
    Created,
    MissingBinding,
    MissingInitialization,
    MissingOutput,
    BoundaryUnavailable,
    ConfigurationMismatch,
}

public enum PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationState
{
    Ready,
    Faulted,
    Disposed,
}

public enum PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
{
    None,
    Reconfigured,
    Cancelled,
    ReconfigurationRejected,
    RollbackFaulted,
    AlreadyRunning,
    Disposed,
}

public sealed record PlaybackIsoBmffRegularMovieWindowsOutputConfiguration(
    PlaybackVideoRendererConfiguration VideoBinding,
    PlaybackAudioDeviceConfiguration AudioBinding,
    PlaybackWindowsVideoStreamConfiguration VideoStream,
    PlaybackWindowsAudioStreamConfiguration AudioStream);

public sealed record
    PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationSnapshot(
        PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationState State,
        PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult Result,
        PlaybackIsoBmffRegularMovieWindowsOutputConfiguration Configuration,
        long ReconfigurationCount,
        bool HasActiveReconfiguration);

internal interface
    IPlaybackIsoBmffRegularMovieWindowsOutputReconfigurationOperations
{
    ValueTask<PlaybackWindowsOutputBufferResetResult> ResetAudioBufferAsync(
        long generation,
        CancellationToken cancellationToken);

    ValueTask<PlaybackWindowsOutputBufferResetResult> ResetVideoBufferAsync(
        long generation,
        CancellationToken cancellationToken);

    ValueTask<PlaybackWindowsOutputStreamResetResult> ResetAudioStreamAsync(
        long generation,
        CancellationToken cancellationToken);

    ValueTask<PlaybackWindowsOutputStreamResetResult> ResetVideoStreamAsync(
        long generation,
        CancellationToken cancellationToken);

    ValueTask<PlaybackWindowsOutputBindingResetResult> ResetBindingAsync(
        long generation,
        CancellationToken cancellationToken);

    ValueTask<PlaybackWindowsOutputBindingResult> ConfigureBindingAsync(
        PlaybackVideoRendererConfiguration video,
        PlaybackAudioDeviceConfiguration audio,
        CancellationToken cancellationToken);

    ValueTask<PlaybackWindowsOutputStreamInitializationResult>
        InitializeStreamsAsync(
            PlaybackWindowsVideoStreamConfiguration video,
            PlaybackWindowsAudioStreamConfiguration audio,
            CancellationToken cancellationToken);
}

internal sealed class
    PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationOperations(
        PlaybackWindowsOutputResourceBindingBoundary binding,
        PlaybackWindowsOutputStreamInitializationBoundary initialization,
        PlaybackWindowsOutputBufferProcessingBoundary output) :
    IPlaybackIsoBmffRegularMovieWindowsOutputReconfigurationOperations
{
    public ValueTask<PlaybackWindowsOutputBufferResetResult>
        ResetAudioBufferAsync(
            long generation,
            CancellationToken cancellationToken) =>
        output.ResetAudioAsync(generation, cancellationToken);

    public ValueTask<PlaybackWindowsOutputBufferResetResult>
        ResetVideoBufferAsync(
            long generation,
            CancellationToken cancellationToken) =>
        output.ResetVideoAsync(generation, cancellationToken);

    public ValueTask<PlaybackWindowsOutputStreamResetResult>
        ResetAudioStreamAsync(
            long generation,
            CancellationToken cancellationToken) =>
        initialization.ResetAudioAsync(generation, cancellationToken);

    public ValueTask<PlaybackWindowsOutputStreamResetResult>
        ResetVideoStreamAsync(
            long generation,
            CancellationToken cancellationToken) =>
        initialization.ResetVideoAsync(generation, cancellationToken);

    public ValueTask<PlaybackWindowsOutputBindingResetResult>
        ResetBindingAsync(
            long generation,
            CancellationToken cancellationToken) =>
        binding.ResetAsync(generation, cancellationToken);

    public ValueTask<PlaybackWindowsOutputBindingResult>
        ConfigureBindingAsync(
            PlaybackVideoRendererConfiguration video,
            PlaybackAudioDeviceConfiguration audio,
            CancellationToken cancellationToken) =>
        binding.ConfigureAsync(video, audio, cancellationToken);

    public ValueTask<PlaybackWindowsOutputStreamInitializationResult>
        InitializeStreamsAsync(
            PlaybackWindowsVideoStreamConfiguration video,
            PlaybackWindowsAudioStreamConfiguration audio,
            CancellationToken cancellationToken) =>
        initialization.InitializeAsync(video, audio, cancellationToken);
}

public sealed class
    PlaybackIsoBmffRegularMovieWindowsOutputReconfiguration : IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly
        IPlaybackIsoBmffRegularMovieWindowsOutputReconfigurationOperations
            _operations;
    private readonly PlaybackWindowsOutputBufferProcessingBoundary? _output;
    private PlaybackIsoBmffRegularMovieWindowsOutputConfiguration
        _configuration;
    private PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationState
        _state =
            PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationState.Ready;
    private PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
        _result =
            PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult.None;
    private CancellationTokenSource? _activeCancellation;
    private long _reconfigurationCount;
    private bool _hasActiveReconfiguration;

    internal PlaybackIsoBmffRegularMovieWindowsOutputReconfiguration(
        PlaybackIsoBmffRegularMovieWindowsOutputConfiguration configuration,
        IPlaybackIsoBmffRegularMovieWindowsOutputReconfigurationOperations
            operations,
        PlaybackWindowsOutputBufferProcessingBoundary? output = null)
    {
        _configuration = configuration;
        _operations = operations;
        _output = output;
    }

    internal bool UsesSink(
        PlaybackIsoBmffRegularMovieWindowsOutputSink sink) =>
        _output is not null && sink.UsesOutput(_output);

    public PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationSnapshot
        Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _state,
                    _result,
                    _configuration,
                    _reconfigurationCount,
                    _hasActiveReconfiguration);
            }
        }
    }

    public async ValueTask<
        PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult>
        ReconfigureAsync(
            long videoGeneration,
            long audioGeneration,
            CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_state ==
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationState
                    .Disposed)
            {
                return SetResultLocked(
                    PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                        .Disposed);
            }

            if (_state ==
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationState
                    .Faulted)
            {
                return _result;
            }
        }

        if (videoGeneration < 0 || audioGeneration < 0)
        {
            return SetResult(
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                    .ReconfigurationRejected);
        }

        bool entered;
        try
        {
            entered = await _gate.WaitAsync(0, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SetResult(
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                    .Cancelled);
        }

        if (!entered)
        {
            return SetResult(
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                    .AlreadyRunning);
        }

        CancellationTokenSource operation;
        PlaybackIsoBmffRegularMovieWindowsOutputConfiguration previous;
        PlaybackIsoBmffRegularMovieWindowsOutputConfiguration requested;
        lock (_sync)
        {
            if (_state ==
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationState
                    .Disposed)
            {
                _gate.Release();
                return SetResultLocked(
                    PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                        .Disposed);
            }

            operation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            _activeCancellation = operation;
            _hasActiveReconfiguration = true;
            previous = _configuration;
            requested = WithGenerations(
                previous,
                videoGeneration,
                audioGeneration);
        }

        try
        {
            var configured = await ResetAndConfigureAsync(
                requested,
                rollback: false,
                operation.Token).ConfigureAwait(false);
            if (configured)
            {
                lock (_sync)
                {
                    _configuration = requested;
                    _reconfigurationCount = checked(
                        _reconfigurationCount + 1);
                    return SetResultLocked(
                        PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                            .Reconfigured);
                }
            }

            var cancelled = operation.IsCancellationRequested;
            if (await ResetAndConfigureAsync(
                    previous,
                    rollback: true,
                    CancellationToken.None).ConfigureAwait(false))
            {
                return SetResult(
                    cancelled
                        ? PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                            .Cancelled
                        : PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                            .ReconfigurationRejected);
            }

            return Fault();
        }
        catch (OverflowException)
        {
            return Fault();
        }
        catch
        {
            if (await ResetAndConfigureAsync(
                    previous,
                    rollback: true,
                    CancellationToken.None).ConfigureAwait(false))
            {
                return SetResult(
                    operation.IsCancellationRequested
                        ? PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                            .Cancelled
                        : PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                            .ReconfigurationRejected);
            }

            return Fault();
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_activeCancellation, operation))
                {
                    _activeCancellation = null;
                }

                _hasActiveReconfiguration = false;
            }

            operation.Dispose();
            _gate.Release();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_state ==
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationState
                    .Disposed)
            {
                return;
            }

            _state =
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationState
                    .Disposed;
            _result =
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                    .Disposed;
            _activeCancellation?.Cancel();
        }

        _gate.Wait();
        _gate.Release();
        GC.SuppressFinalize(this);
    }

    private async ValueTask<bool> ResetAndConfigureAsync(
        PlaybackIsoBmffRegularMovieWindowsOutputConfiguration configuration,
        bool rollback,
        CancellationToken cancellationToken)
    {
        try
        {
            var audioGeneration = configuration.AudioBinding.Generation;
            var videoGeneration = configuration.VideoBinding.Generation;
            if (!IsBufferReset(await _operations.ResetAudioBufferAsync(
                    audioGeneration,
                    cancellationToken).ConfigureAwait(false)))
            {
                return false;
            }

            if (!IsBufferReset(await _operations.ResetVideoBufferAsync(
                    videoGeneration,
                    cancellationToken).ConfigureAwait(false)))
            {
                return false;
            }

            if (!IsStreamReset(await _operations.ResetAudioStreamAsync(
                    audioGeneration,
                    cancellationToken).ConfigureAwait(false), rollback))
            {
                return false;
            }

            if (!IsStreamReset(await _operations.ResetVideoStreamAsync(
                    videoGeneration,
                    cancellationToken).ConfigureAwait(false), rollback))
            {
                return false;
            }

            if (!IsBindingReset(await _operations.ResetBindingAsync(
                    Math.Max(videoGeneration, audioGeneration),
                    cancellationToken).ConfigureAwait(false), rollback))
            {
                return false;
            }

            if (await _operations.ConfigureBindingAsync(
                    configuration.VideoBinding,
                    configuration.AudioBinding,
                    cancellationToken).ConfigureAwait(false) !=
                PlaybackWindowsOutputBindingResult.Bound)
            {
                return false;
            }

            return await _operations.InitializeStreamsAsync(
                    configuration.VideoStream,
                    configuration.AudioStream,
                    cancellationToken).ConfigureAwait(false) ==
                PlaybackWindowsOutputStreamInitializationResult.Initialized;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsBufferReset(
        PlaybackWindowsOutputBufferResetResult result) =>
        result is PlaybackWindowsOutputBufferResetResult.Reset or
            PlaybackWindowsOutputBufferResetResult.DeviceLost;

    private static bool IsStreamReset(
        PlaybackWindowsOutputStreamResetResult result,
        bool rollback) =>
        result is PlaybackWindowsOutputStreamResetResult.Reset or
            PlaybackWindowsOutputStreamResetResult.DeviceLost ||
        rollback && result ==
            PlaybackWindowsOutputStreamResetResult.NotInitialized;

    private static bool IsBindingReset(
        PlaybackWindowsOutputBindingResetResult result,
        bool rollback) =>
        result is PlaybackWindowsOutputBindingResetResult.Reset or
            PlaybackWindowsOutputBindingResetResult.DeviceLost ||
        rollback && result == PlaybackWindowsOutputBindingResetResult.NotBound;

    private static
        PlaybackIsoBmffRegularMovieWindowsOutputConfiguration WithGenerations(
            PlaybackIsoBmffRegularMovieWindowsOutputConfiguration source,
            long videoGeneration,
            long audioGeneration) =>
        new(
            source.VideoBinding with { Generation = videoGeneration },
            source.AudioBinding with { Generation = audioGeneration },
            source.VideoStream with { Generation = videoGeneration },
            source.AudioStream with { Generation = audioGeneration });

    private PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
        Fault()
    {
        lock (_sync)
        {
            _state =
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationState
                    .Faulted;
            return SetResultLocked(
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                    .RollbackFaulted);
        }
    }

    private PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
        SetResult(
            PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                result)
    {
        lock (_sync)
        {
            return SetResultLocked(result);
        }
    }

    private PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
        SetResultLocked(
            PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                result)
    {
        _result = result;
        return result;
    }
}

public sealed record
    PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationCreation(
        PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationCreationResult
            Result,
        PlaybackIsoBmffRegularMovieWindowsOutputReconfiguration?
            Reconfiguration);

public static class
    PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationFactory
{
    public static
        PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationCreation Create(
            PlaybackWindowsOutputResourceBindingBoundary? binding,
            PlaybackWindowsOutputStreamInitializationBoundary? initialization,
            PlaybackWindowsOutputBufferProcessingBoundary? output,
            PlaybackIsoBmffRegularMovieWindowsOutputConfiguration?
                configuration)
    {
        if (binding is null)
        {
            return Failure(
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationCreationResult
                    .MissingBinding);
        }

        if (initialization is null)
        {
            return Failure(
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationCreationResult
                    .MissingInitialization);
        }

        if (output is null)
        {
            return Failure(
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationCreationResult
                    .MissingOutput);
        }

        var bindingSnapshot = binding.Snapshot;
        var streamSnapshot = initialization.Snapshot;
        var outputSnapshot = output.Snapshot;
        if (bindingSnapshot.VideoState !=
                PlaybackWindowsOutputBindingState.Bound ||
            bindingSnapshot.AudioState !=
                PlaybackWindowsOutputBindingState.Bound ||
            streamSnapshot.VideoState !=
                PlaybackWindowsOutputStreamState.Initialized ||
            streamSnapshot.AudioState !=
                PlaybackWindowsOutputStreamState.Initialized ||
            outputSnapshot.VideoState ==
                PlaybackWindowsOutputBufferState.Disposed ||
            outputSnapshot.AudioState ==
                PlaybackWindowsOutputBufferState.Disposed)
        {
            return Failure(
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationCreationResult
                    .BoundaryUnavailable);
        }

        if (configuration is null ||
            !Matches(
                configuration,
                bindingSnapshot,
                streamSnapshot,
                outputSnapshot))
        {
            return Failure(
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationCreationResult
                    .ConfigurationMismatch);
        }

        return new(
            PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationCreationResult
                .Created,
            new PlaybackIsoBmffRegularMovieWindowsOutputReconfiguration(
                configuration,
                new PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationOperations(
                    binding,
                    initialization,
                    output),
                output));
    }

    private static bool Matches(
        PlaybackIsoBmffRegularMovieWindowsOutputConfiguration configuration,
        PlaybackWindowsOutputBindingSnapshot binding,
        PlaybackWindowsOutputStreamSnapshot streams,
        PlaybackWindowsOutputBufferSnapshot output) =>
        configuration.VideoBinding.Generation == binding.VideoGeneration &&
        configuration.AudioBinding.Generation == binding.AudioGeneration &&
        configuration.VideoStream.Generation == streams.VideoGeneration &&
        configuration.AudioStream.Generation == streams.AudioGeneration &&
        binding.VideoGeneration == streams.VideoGeneration &&
        binding.AudioGeneration == streams.AudioGeneration &&
        output.VideoGeneration == streams.VideoGeneration &&
        output.AudioGeneration == streams.AudioGeneration &&
        configuration.VideoBinding.PixelFormat ==
            configuration.VideoStream.PixelFormat &&
        configuration.VideoBinding.Width == configuration.VideoStream.Width &&
        configuration.VideoBinding.Height ==
            configuration.VideoStream.Height &&
        configuration.AudioBinding.SampleFormat ==
            configuration.AudioStream.SampleFormat &&
        configuration.AudioBinding.SampleRate ==
            configuration.AudioStream.SampleRate &&
        configuration.AudioBinding.ChannelCount ==
            configuration.AudioStream.ChannelCount &&
        streams.AudioSampleFormat == configuration.AudioStream.SampleFormat &&
        streams.AudioSampleRate == configuration.AudioStream.SampleRate &&
        streams.AudioChannelCount == configuration.AudioStream.ChannelCount;

    private static
        PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationCreation
        Failure(
            PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationCreationResult
                result) =>
        new(result, null);
}
