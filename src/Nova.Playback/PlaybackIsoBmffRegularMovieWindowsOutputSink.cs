namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffRegularMovieWindowsOutputSinkCreationResult
{
    Created,
    MissingOutput,
    OutputUnavailable,
    InvalidGeneration,
}

public enum PlaybackIsoBmffRegularMovieWindowsOutputSinkState
{
    Accepting,
    Faulted,
    Disposed,
}

public enum PlaybackIsoBmffRegularMovieWindowsOutputSinkResult
{
    None,
    VideoProcessed,
    AudioProcessed,
    Cancelled,
    OutputRejected,
    Faulted,
    Disposed,
}

public sealed record PlaybackIsoBmffRegularMovieWindowsOutputSinkSnapshot(
    PlaybackIsoBmffRegularMovieWindowsOutputSinkState State,
    PlaybackIsoBmffRegularMovieWindowsOutputSinkResult Result,
    long VideoGeneration,
    long AudioGeneration,
    long ProcessedVideoCount,
    long ProcessedAudioCount,
    PlaybackWindowsOutputBufferSnapshot Output);

internal interface IPlaybackIsoBmffRegularMovieWindowsOutputSinkOperations
{
    ValueTask<PlaybackWindowsOutputBufferProcessingResult> ProcessVideoAsync(
        PlaybackWindowsOutputBufferProcessingBoundary output,
        PlaybackWindowsVideoFrameBuffer frame,
        CancellationToken cancellationToken);

    ValueTask<PlaybackWindowsOutputBufferProcessingResult> ProcessAudioAsync(
        PlaybackWindowsOutputBufferProcessingBoundary output,
        PlaybackWindowsAudioSampleBuffer samples,
        CancellationToken cancellationToken);
}

internal sealed class PlaybackIsoBmffRegularMovieWindowsOutputSinkOperations :
    IPlaybackIsoBmffRegularMovieWindowsOutputSinkOperations
{
    public ValueTask<PlaybackWindowsOutputBufferProcessingResult>
        ProcessVideoAsync(
            PlaybackWindowsOutputBufferProcessingBoundary output,
            PlaybackWindowsVideoFrameBuffer frame,
            CancellationToken cancellationToken) =>
        output.ProcessVideoAsync(frame, cancellationToken);

    public ValueTask<PlaybackWindowsOutputBufferProcessingResult>
        ProcessAudioAsync(
            PlaybackWindowsOutputBufferProcessingBoundary output,
            PlaybackWindowsAudioSampleBuffer samples,
            CancellationToken cancellationToken) =>
        output.ProcessAudioAsync(samples, cancellationToken);
}

public sealed class PlaybackIsoBmffRegularMovieWindowsOutputSink :
    IPlaybackDecodedFrameSink,
    IDisposable,
    IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly PlaybackWindowsOutputBufferProcessingBoundary _output;
    private readonly IPlaybackIsoBmffRegularMovieWindowsOutputSinkOperations
        _operations;
    private long _videoGeneration;
    private long _audioGeneration;
    private PlaybackIsoBmffRegularMovieWindowsOutputSinkState _state =
        PlaybackIsoBmffRegularMovieWindowsOutputSinkState.Accepting;
    private PlaybackIsoBmffRegularMovieWindowsOutputSinkResult _result =
        PlaybackIsoBmffRegularMovieWindowsOutputSinkResult.None;
    private long _processedVideoCount;
    private long _processedAudioCount;

    internal PlaybackIsoBmffRegularMovieWindowsOutputSink(
        PlaybackWindowsOutputBufferProcessingBoundary output,
        long videoGeneration,
        long audioGeneration,
        IPlaybackIsoBmffRegularMovieWindowsOutputSinkOperations operations)
    {
        _output = output;
        _videoGeneration = videoGeneration;
        _audioGeneration = audioGeneration;
        _operations = operations;
    }

    public PlaybackIsoBmffRegularMovieWindowsOutputSinkSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _state,
                    _result,
                    _videoGeneration,
                    _audioGeneration,
                    _processedVideoCount,
                    _processedAudioCount,
                    _output.Snapshot);
            }
        }
    }

    public ValueTask WriteAudioAsync(
        PlaybackDecodedAudioFrame frame,
        CancellationToken cancellationToken) =>
        WriteAsync(
            video: false,
            () => _operations.ProcessAudioAsync(
                _output,
                new(
                    frame.SampleFormat,
                    frame.SampleRate,
                    frame.ChannelCount,
                    frame.SampleCount,
                    frame.Payload,
                    _audioGeneration),
                cancellationToken),
            cancellationToken);

    public ValueTask WriteVideoAsync(
        PlaybackDecodedVideoFrame frame,
        CancellationToken cancellationToken) =>
        WriteAsync(
            video: true,
            () => _operations.ProcessVideoAsync(
                _output,
                new(
                    frame.PixelFormat,
                    frame.Width,
                    frame.Height,
                    frame.Stride,
                    frame.Payload,
                    _videoGeneration),
                cancellationToken),
            cancellationToken);

    internal bool UsesOutput(
        PlaybackWindowsOutputBufferProcessingBoundary output) =>
        ReferenceEquals(_output, output);

    internal bool CommitReconfiguration(
        long videoGeneration,
        long audioGeneration)
    {
        lock (_sync)
        {
            if (_state !=
                    PlaybackIsoBmffRegularMovieWindowsOutputSinkState
                        .Accepting ||
                videoGeneration < 0 ||
                audioGeneration < 0)
            {
                return false;
            }

            var output = _output.Snapshot;
            if (output.VideoState != PlaybackWindowsOutputBufferState.Ready ||
                output.AudioState != PlaybackWindowsOutputBufferState.Ready ||
                output.VideoGeneration != videoGeneration ||
                output.AudioGeneration != audioGeneration)
            {
                return false;
            }

            _videoGeneration = videoGeneration;
            _audioGeneration = audioGeneration;
            _result = PlaybackIsoBmffRegularMovieWindowsOutputSinkResult.None;
            return true;
        }
    }

    public void Dispose()
    {
        _writeGate.Wait();
        try
        {
            lock (_sync)
            {
                if (_state ==
                    PlaybackIsoBmffRegularMovieWindowsOutputSinkState.Disposed)
                {
                    return;
                }

                _state =
                    PlaybackIsoBmffRegularMovieWindowsOutputSinkState.Disposed;
                _result =
                    PlaybackIsoBmffRegularMovieWindowsOutputSinkResult.Disposed;
            }
        }
        finally
        {
            _writeGate.Release();
        }

        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private async ValueTask WriteAsync(
        bool video,
        Func<ValueTask<PlaybackWindowsOutputBufferProcessingResult>> write,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_state ==
                PlaybackIsoBmffRegularMovieWindowsOutputSinkState.Disposed)
            {
                ObjectDisposedException.ThrowIf(true, this);
            }

            if (_state ==
                PlaybackIsoBmffRegularMovieWindowsOutputSinkState.Faulted)
            {
                throw new InvalidOperationException(
                    "The regular-movie Windows output sink is faulted.");
            }
        }

        try
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            SetResult(
                PlaybackIsoBmffRegularMovieWindowsOutputSinkResult.Cancelled);
            throw;
        }

        try
        {
            lock (_sync)
            {
                if (_state ==
                    PlaybackIsoBmffRegularMovieWindowsOutputSinkState.Disposed)
                {
                    ObjectDisposedException.ThrowIf(true, this);
                }
            }

            PlaybackWindowsOutputBufferProcessingResult result;
            try
            {
                result = await write().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                SetResult(
                    PlaybackIsoBmffRegularMovieWindowsOutputSinkResult
                        .Cancelled);
                throw;
            }
            catch
            {
                Fault(
                    PlaybackIsoBmffRegularMovieWindowsOutputSinkResult.Faulted);
                throw;
            }

            if (result == PlaybackWindowsOutputBufferProcessingResult.Cancelled &&
                cancellationToken.IsCancellationRequested)
            {
                SetResult(
                    PlaybackIsoBmffRegularMovieWindowsOutputSinkResult
                        .Cancelled);
                throw new OperationCanceledException(cancellationToken);
            }

            if (result != PlaybackWindowsOutputBufferProcessingResult.Processed)
            {
                Fault(
                    PlaybackIsoBmffRegularMovieWindowsOutputSinkResult
                        .OutputRejected);
                throw new InvalidOperationException(
                    $"Windows output rejected the canonical frame: {result}.");
            }

            lock (_sync)
            {
                if (video)
                {
                    _processedVideoCount = checked(_processedVideoCount + 1);
                    _result =
                        PlaybackIsoBmffRegularMovieWindowsOutputSinkResult
                            .VideoProcessed;
                }
                else
                {
                    _processedAudioCount = checked(_processedAudioCount + 1);
                    _result =
                        PlaybackIsoBmffRegularMovieWindowsOutputSinkResult
                            .AudioProcessed;
                }
            }
        }
        catch (OverflowException)
        {
            Fault(
                PlaybackIsoBmffRegularMovieWindowsOutputSinkResult.Faulted);
            throw;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void SetResult(
        PlaybackIsoBmffRegularMovieWindowsOutputSinkResult result)
    {
        lock (_sync)
        {
            _result = result;
        }
    }

    private void Fault(
        PlaybackIsoBmffRegularMovieWindowsOutputSinkResult result)
    {
        lock (_sync)
        {
            _state =
                PlaybackIsoBmffRegularMovieWindowsOutputSinkState.Faulted;
            _result = result;
        }
    }
}

public sealed record PlaybackIsoBmffRegularMovieWindowsOutputSinkCreation(
    PlaybackIsoBmffRegularMovieWindowsOutputSinkCreationResult Result,
    PlaybackIsoBmffRegularMovieWindowsOutputSink? Sink);

public sealed class PlaybackIsoBmffRegularMovieWindowsOutputSinkFactory
{
    private readonly IPlaybackIsoBmffRegularMovieWindowsOutputSinkOperations
        _operations;

    public PlaybackIsoBmffRegularMovieWindowsOutputSinkFactory()
        : this(new PlaybackIsoBmffRegularMovieWindowsOutputSinkOperations())
    {
    }

    internal PlaybackIsoBmffRegularMovieWindowsOutputSinkFactory(
        IPlaybackIsoBmffRegularMovieWindowsOutputSinkOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public PlaybackIsoBmffRegularMovieWindowsOutputSinkCreation Create(
        PlaybackWindowsOutputBufferProcessingBoundary? output,
        long videoGeneration,
        long audioGeneration)
    {
        if (output is null)
        {
            return new(
                PlaybackIsoBmffRegularMovieWindowsOutputSinkCreationResult
                    .MissingOutput,
                null);
        }

        if (videoGeneration < 0 || audioGeneration < 0)
        {
            return new(
                PlaybackIsoBmffRegularMovieWindowsOutputSinkCreationResult
                    .InvalidGeneration,
                null);
        }

        var snapshot = output.Snapshot;
        if (snapshot.VideoState == PlaybackWindowsOutputBufferState.Disposed ||
            snapshot.AudioState == PlaybackWindowsOutputBufferState.Disposed)
        {
            return new(
                PlaybackIsoBmffRegularMovieWindowsOutputSinkCreationResult
                    .OutputUnavailable,
                null);
        }

        return new(
            PlaybackIsoBmffRegularMovieWindowsOutputSinkCreationResult.Created,
            new PlaybackIsoBmffRegularMovieWindowsOutputSink(
                output,
                videoGeneration,
                audioGeneration,
                _operations));
    }
}
