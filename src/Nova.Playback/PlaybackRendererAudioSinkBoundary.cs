namespace PeerCast.Nova.Playback;

public enum PlaybackOutputBackendResult
{
    Success,
    FormatUnsupported,
    DeviceLost,
    TemporarilyUnavailable,
    Rejected,
}

public enum PlaybackOutputConfigureResult
{
    Configured,
    InvalidConfiguration,
    FormatUnsupported,
    DeviceLost,
    TemporarilyUnavailable,
    Rejected,
    Cancelled,
    Disposed,
    Failure,
}

public enum PlaybackOutputSubmitResult
{
    Submitted,
    NotConfigured,
    InvalidFrame,
    FormatUnsupported,
    GenerationMismatch,
    DeviceLost,
    TemporarilyUnavailable,
    Rejected,
    Cancelled,
    Disposed,
    Failure,
}

public enum PlaybackOutputFlushResult
{
    Flushed,
    NotConfigured,
    DeviceLost,
    TemporarilyUnavailable,
    Rejected,
    Cancelled,
    Disposed,
    Failure,
}

public enum PlaybackOutputResetResult
{
    Reset,
    InvalidGeneration,
    DeviceLost,
    TemporarilyUnavailable,
    Rejected,
    Cancelled,
    Disposed,
    Failure,
}

public readonly record struct PlaybackVideoRendererConfiguration(
    PlaybackDecodedVideoPixelFormat PixelFormat,
    int Width,
    int Height,
    int Stride,
    long Generation);

public readonly record struct PlaybackAudioDeviceConfiguration(
    PlaybackDecodedAudioSampleFormat SampleFormat,
    int SampleRate,
    int ChannelCount,
    long Generation);

public interface IPlaybackVideoRenderer
{
    ValueTask<PlaybackOutputBackendResult> ConfigureAsync(
        PlaybackVideoRendererConfiguration configuration,
        CancellationToken cancellationToken);

    ValueTask<PlaybackOutputBackendResult> SubmitAsync(
        PlaybackDecodedVideoFrame frame,
        long generation,
        CancellationToken cancellationToken);

    ValueTask<PlaybackOutputBackendResult> FlushAsync(CancellationToken cancellationToken);

    ValueTask<PlaybackOutputBackendResult> ResetAsync(
        long generation,
        CancellationToken cancellationToken);
}

public interface IPlaybackAudioDeviceSink
{
    ValueTask<PlaybackOutputBackendResult> ConfigureAsync(
        PlaybackAudioDeviceConfiguration configuration,
        CancellationToken cancellationToken);

    ValueTask<PlaybackOutputBackendResult> SubmitAsync(
        PlaybackDecodedAudioFrame frame,
        long generation,
        CancellationToken cancellationToken);

    ValueTask<PlaybackOutputBackendResult> FlushAsync(CancellationToken cancellationToken);

    ValueTask<PlaybackOutputBackendResult> ResetAsync(
        long generation,
        CancellationToken cancellationToken);
}

public sealed class PlaybackVideoRendererBoundary : IDisposable
{
    public const int MaximumPayloadBytes = 256 * 1024 * 1024;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IPlaybackVideoRenderer _renderer;
    private PlaybackVideoRendererConfiguration _configuration;
    private bool _isConfigured;
    private int _isDisposed;

    public PlaybackVideoRendererBoundary(IPlaybackVideoRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    public async ValueTask<PlaybackOutputConfigureResult> ConfigureAsync(
        PlaybackVideoRendererConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!IsValid(configuration))
        {
            return PlaybackOutputConfigureResult.InvalidConfiguration;
        }

        var entered = await EnterAsync(cancellationToken).ConfigureAwait(false);
        if (entered != EntryResult.Entered)
        {
            return entered == EntryResult.Cancelled
                ? PlaybackOutputConfigureResult.Cancelled
                : PlaybackOutputConfigureResult.Disposed;
        }

        try
        {
            var result = await InvokeAsync(
                token => _renderer.ConfigureAsync(configuration, token),
                cancellationToken).ConfigureAwait(false);
            if (result == PlaybackOutputBackendResult.Success)
            {
                _configuration = configuration;
                _isConfigured = true;
            }

            return MapConfigure(result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<PlaybackOutputSubmitResult> SubmitAsync(
        PlaybackDecodedVideoFrame frame,
        long generation,
        CancellationToken cancellationToken)
    {
        var entered = await EnterAsync(cancellationToken).ConfigureAwait(false);
        if (entered != EntryResult.Entered)
        {
            return entered == EntryResult.Cancelled
                ? PlaybackOutputSubmitResult.Cancelled
                : PlaybackOutputSubmitResult.Disposed;
        }

        try
        {
            if (!_isConfigured)
            {
                return PlaybackOutputSubmitResult.NotConfigured;
            }

            if (generation != _configuration.Generation)
            {
                return PlaybackOutputSubmitResult.GenerationMismatch;
            }

            if (!IsValid(frame, _configuration))
            {
                return !Enum.IsDefined(frame.PixelFormat) ||
                    frame.PixelFormat != PlaybackDecodedVideoPixelFormat.Bgra32
                    ? PlaybackOutputSubmitResult.FormatUnsupported
                    : PlaybackOutputSubmitResult.InvalidFrame;
            }

            var result = await InvokeAsync(
                token => _renderer.SubmitAsync(frame, generation, token),
                cancellationToken).ConfigureAwait(false);
            return MapSubmit(result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<PlaybackOutputFlushResult> FlushAsync(
        CancellationToken cancellationToken)
    {
        var entered = await EnterAsync(cancellationToken).ConfigureAwait(false);
        if (entered != EntryResult.Entered)
        {
            return entered == EntryResult.Cancelled
                ? PlaybackOutputFlushResult.Cancelled
                : PlaybackOutputFlushResult.Disposed;
        }

        try
        {
            if (!_isConfigured)
            {
                return PlaybackOutputFlushResult.NotConfigured;
            }

            return MapFlush(await InvokeAsync(
                _renderer.FlushAsync,
                cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<PlaybackOutputResetResult> ResetAsync(
        long generation,
        CancellationToken cancellationToken)
    {
        if (generation < 0)
        {
            return PlaybackOutputResetResult.InvalidGeneration;
        }

        var entered = await EnterAsync(cancellationToken).ConfigureAwait(false);
        if (entered != EntryResult.Entered)
        {
            return entered == EntryResult.Cancelled
                ? PlaybackOutputResetResult.Cancelled
                : PlaybackOutputResetResult.Disposed;
        }

        try
        {
            var result = await InvokeAsync(
                token => _renderer.ResetAsync(generation, token),
                cancellationToken).ConfigureAwait(false);
            if (result == PlaybackOutputBackendResult.Success)
            {
                _configuration = default;
                _isConfigured = false;
            }

            return MapReset(result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _isDisposed, 1);
        GC.SuppressFinalize(this);
    }

    private async ValueTask<EntryResult> EnterAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return EntryResult.Disposed;
        }

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return EntryResult.Cancelled;
        }

        if (Volatile.Read(ref _isDisposed) == 0)
        {
            return EntryResult.Entered;
        }

        _gate.Release();
        return EntryResult.Disposed;
    }

    private async ValueTask<PlaybackOutputBackendResult> InvokeAsync(
        Func<CancellationToken, ValueTask<PlaybackOutputBackendResult>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await operation(cancellationToken).ConfigureAwait(false);
            if (Enum.IsDefined(result))
            {
                return result;
            }

            Interlocked.Exchange(ref _isDisposed, 1);
            return (PlaybackOutputBackendResult)(-1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (PlaybackOutputBackendResult)(-2);
        }
        catch
        {
            return (PlaybackOutputBackendResult)(-1);
        }
    }

    private static bool IsValid(PlaybackVideoRendererConfiguration value)
    {
        if (!Enum.IsDefined(value.PixelFormat) ||
            value.PixelFormat != PlaybackDecodedVideoPixelFormat.Bgra32 ||
            value.Width is < 1 or > PlaybackDecodedVideoFrame.MaximumDimension ||
            value.Height is < 1 or > PlaybackDecodedVideoFrame.MaximumDimension ||
            value.Generation < 0)
        {
            return false;
        }

        try
        {
            var minimumStride = checked(value.Width * 4);
            var length = checked((long)value.Stride * value.Height);
            return value.Stride >= minimumStride &&
                value.Stride <= PlaybackDecodedVideoFrame.MaximumStride &&
                length is > 0 and <= MaximumPayloadBytes;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsValid(
        PlaybackDecodedVideoFrame frame,
        PlaybackVideoRendererConfiguration configuration)
    {
        try
        {
            var length = checked((long)frame.Stride * frame.Height);
            return frame.Sequence >= 0 &&
                Enum.IsDefined(frame.Discontinuity) &&
                Enum.IsDefined(frame.PixelFormat) &&
                frame.PixelFormat == PlaybackDecodedVideoPixelFormat.Bgra32 &&
                frame.PixelFormat == configuration.PixelFormat &&
                frame.Width == configuration.Width &&
                frame.Height == configuration.Height &&
                frame.Stride == configuration.Stride &&
                frame.Duration > TimeSpan.Zero &&
                frame.Duration <= PlaybackDecodedVideoFrame.MaximumDuration &&
                length is > 0 and <= MaximumPayloadBytes &&
                frame.Payload.Length == length;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static PlaybackOutputConfigureResult MapConfigure(PlaybackOutputBackendResult result) =>
        result switch
        {
            PlaybackOutputBackendResult.Success => PlaybackOutputConfigureResult.Configured,
            PlaybackOutputBackendResult.FormatUnsupported =>
                PlaybackOutputConfigureResult.FormatUnsupported,
            PlaybackOutputBackendResult.DeviceLost => PlaybackOutputConfigureResult.DeviceLost,
            PlaybackOutputBackendResult.TemporarilyUnavailable =>
                PlaybackOutputConfigureResult.TemporarilyUnavailable,
            PlaybackOutputBackendResult.Rejected => PlaybackOutputConfigureResult.Rejected,
            (PlaybackOutputBackendResult)(-2) => PlaybackOutputConfigureResult.Cancelled,
            _ => PlaybackOutputConfigureResult.Failure,
        };

    private static PlaybackOutputSubmitResult MapSubmit(PlaybackOutputBackendResult result) =>
        result switch
        {
            PlaybackOutputBackendResult.Success => PlaybackOutputSubmitResult.Submitted,
            PlaybackOutputBackendResult.FormatUnsupported =>
                PlaybackOutputSubmitResult.FormatUnsupported,
            PlaybackOutputBackendResult.DeviceLost => PlaybackOutputSubmitResult.DeviceLost,
            PlaybackOutputBackendResult.TemporarilyUnavailable =>
                PlaybackOutputSubmitResult.TemporarilyUnavailable,
            PlaybackOutputBackendResult.Rejected => PlaybackOutputSubmitResult.Rejected,
            (PlaybackOutputBackendResult)(-2) => PlaybackOutputSubmitResult.Cancelled,
            _ => PlaybackOutputSubmitResult.Failure,
        };

    private static PlaybackOutputFlushResult MapFlush(PlaybackOutputBackendResult result) =>
        result switch
        {
            PlaybackOutputBackendResult.Success => PlaybackOutputFlushResult.Flushed,
            PlaybackOutputBackendResult.DeviceLost => PlaybackOutputFlushResult.DeviceLost,
            PlaybackOutputBackendResult.TemporarilyUnavailable =>
                PlaybackOutputFlushResult.TemporarilyUnavailable,
            PlaybackOutputBackendResult.Rejected => PlaybackOutputFlushResult.Rejected,
            (PlaybackOutputBackendResult)(-2) => PlaybackOutputFlushResult.Cancelled,
            _ => PlaybackOutputFlushResult.Failure,
        };

    private static PlaybackOutputResetResult MapReset(PlaybackOutputBackendResult result) =>
        result switch
        {
            PlaybackOutputBackendResult.Success => PlaybackOutputResetResult.Reset,
            PlaybackOutputBackendResult.DeviceLost => PlaybackOutputResetResult.DeviceLost,
            PlaybackOutputBackendResult.TemporarilyUnavailable =>
                PlaybackOutputResetResult.TemporarilyUnavailable,
            PlaybackOutputBackendResult.Rejected => PlaybackOutputResetResult.Rejected,
            (PlaybackOutputBackendResult)(-2) => PlaybackOutputResetResult.Cancelled,
            _ => PlaybackOutputResetResult.Failure,
        };

    private enum EntryResult
    {
        Entered,
        Cancelled,
        Disposed,
    }
}

public sealed class PlaybackAudioDeviceSinkBoundary : IDisposable
{
    public const int MaximumPayloadBytes = PlaybackVideoRendererBoundary.MaximumPayloadBytes;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IPlaybackAudioDeviceSink _sink;
    private PlaybackAudioDeviceConfiguration _configuration;
    private bool _isConfigured;
    private int _isDisposed;

    public PlaybackAudioDeviceSinkBoundary(IPlaybackAudioDeviceSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
    }

    public async ValueTask<PlaybackOutputConfigureResult> ConfigureAsync(
        PlaybackAudioDeviceConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!IsValid(configuration))
        {
            return PlaybackOutputConfigureResult.InvalidConfiguration;
        }

        var entered = await EnterAsync(cancellationToken).ConfigureAwait(false);
        if (entered != EntryResult.Entered)
        {
            return entered == EntryResult.Cancelled
                ? PlaybackOutputConfigureResult.Cancelled
                : PlaybackOutputConfigureResult.Disposed;
        }

        try
        {
            var result = await InvokeAsync(
                token => _sink.ConfigureAsync(configuration, token),
                cancellationToken).ConfigureAwait(false);
            if (result == PlaybackOutputBackendResult.Success)
            {
                _configuration = configuration;
                _isConfigured = true;
            }

            return MapConfigure(result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<PlaybackOutputSubmitResult> SubmitAsync(
        PlaybackDecodedAudioFrame frame,
        long generation,
        CancellationToken cancellationToken)
    {
        var entered = await EnterAsync(cancellationToken).ConfigureAwait(false);
        if (entered != EntryResult.Entered)
        {
            return entered == EntryResult.Cancelled
                ? PlaybackOutputSubmitResult.Cancelled
                : PlaybackOutputSubmitResult.Disposed;
        }

        try
        {
            if (!_isConfigured)
            {
                return PlaybackOutputSubmitResult.NotConfigured;
            }

            if (generation != _configuration.Generation)
            {
                return PlaybackOutputSubmitResult.GenerationMismatch;
            }

            if (!IsValid(frame, _configuration))
            {
                return !IsSupported(frame.SampleFormat)
                    ? PlaybackOutputSubmitResult.FormatUnsupported
                    : PlaybackOutputSubmitResult.InvalidFrame;
            }

            return MapSubmit(await InvokeAsync(
                token => _sink.SubmitAsync(frame, generation, token),
                cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<PlaybackOutputFlushResult> FlushAsync(
        CancellationToken cancellationToken)
    {
        var entered = await EnterAsync(cancellationToken).ConfigureAwait(false);
        if (entered != EntryResult.Entered)
        {
            return entered == EntryResult.Cancelled
                ? PlaybackOutputFlushResult.Cancelled
                : PlaybackOutputFlushResult.Disposed;
        }

        try
        {
            return !_isConfigured
                ? PlaybackOutputFlushResult.NotConfigured
                : MapFlush(await InvokeAsync(_sink.FlushAsync, cancellationToken)
                    .ConfigureAwait(false));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<PlaybackOutputResetResult> ResetAsync(
        long generation,
        CancellationToken cancellationToken)
    {
        if (generation < 0)
        {
            return PlaybackOutputResetResult.InvalidGeneration;
        }

        var entered = await EnterAsync(cancellationToken).ConfigureAwait(false);
        if (entered != EntryResult.Entered)
        {
            return entered == EntryResult.Cancelled
                ? PlaybackOutputResetResult.Cancelled
                : PlaybackOutputResetResult.Disposed;
        }

        try
        {
            var result = await InvokeAsync(
                token => _sink.ResetAsync(generation, token),
                cancellationToken).ConfigureAwait(false);
            if (result == PlaybackOutputBackendResult.Success)
            {
                _configuration = default;
                _isConfigured = false;
            }

            return MapReset(result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _isDisposed, 1);
        GC.SuppressFinalize(this);
    }

    private async ValueTask<EntryResult> EnterAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return EntryResult.Disposed;
        }

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return EntryResult.Cancelled;
        }

        if (Volatile.Read(ref _isDisposed) == 0)
        {
            return EntryResult.Entered;
        }

        _gate.Release();
        return EntryResult.Disposed;
    }

    private async ValueTask<PlaybackOutputBackendResult> InvokeAsync(
        Func<CancellationToken, ValueTask<PlaybackOutputBackendResult>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await operation(cancellationToken).ConfigureAwait(false);
            if (Enum.IsDefined(result))
            {
                return result;
            }

            Interlocked.Exchange(ref _isDisposed, 1);
            return (PlaybackOutputBackendResult)(-1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (PlaybackOutputBackendResult)(-2);
        }
        catch
        {
            return (PlaybackOutputBackendResult)(-1);
        }
    }

    private static bool IsValid(PlaybackAudioDeviceConfiguration value) =>
        IsSupported(value.SampleFormat) &&
        value.SampleRate is >= PlaybackDecodedAudioFrame.MinimumSampleRate and
            <= PlaybackDecodedAudioFrame.MaximumSampleRate &&
        value.ChannelCount is >= PlaybackDecodedAudioFrame.MinimumChannelCount and
            <= PlaybackDecodedAudioFrame.MaximumChannelCount &&
        value.Generation >= 0;

    private static bool IsValid(
        PlaybackDecodedAudioFrame frame,
        PlaybackAudioDeviceConfiguration configuration)
    {
        try
        {
            var bytesPerSample = frame.SampleFormat switch
            {
                PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved => 2,
                PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved => 4,
                _ => 0,
            };
            var length = checked((long)frame.SampleCount * frame.ChannelCount * bytesPerSample);
            return bytesPerSample != 0 &&
                frame.Sequence >= 0 &&
                Enum.IsDefined(frame.Discontinuity) &&
                frame.SampleFormat == configuration.SampleFormat &&
                frame.SampleRate == configuration.SampleRate &&
                frame.ChannelCount == configuration.ChannelCount &&
                frame.SampleCount is >= 1 and <= PlaybackDecodedAudioFrame.MaximumSampleCount &&
                frame.Duration > TimeSpan.Zero &&
                frame.Duration <= PlaybackDecodedAudioFrame.MaximumDuration &&
                length is > 0 and <= MaximumPayloadBytes &&
                frame.Payload.Length == length;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsSupported(PlaybackDecodedAudioSampleFormat format) =>
        Enum.IsDefined(format) && format is
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved or
            PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved;

    private static PlaybackOutputConfigureResult MapConfigure(PlaybackOutputBackendResult result) =>
        result switch
        {
            PlaybackOutputBackendResult.Success => PlaybackOutputConfigureResult.Configured,
            PlaybackOutputBackendResult.FormatUnsupported =>
                PlaybackOutputConfigureResult.FormatUnsupported,
            PlaybackOutputBackendResult.DeviceLost => PlaybackOutputConfigureResult.DeviceLost,
            PlaybackOutputBackendResult.TemporarilyUnavailable =>
                PlaybackOutputConfigureResult.TemporarilyUnavailable,
            PlaybackOutputBackendResult.Rejected => PlaybackOutputConfigureResult.Rejected,
            (PlaybackOutputBackendResult)(-2) => PlaybackOutputConfigureResult.Cancelled,
            _ => PlaybackOutputConfigureResult.Failure,
        };

    private static PlaybackOutputSubmitResult MapSubmit(PlaybackOutputBackendResult result) =>
        result switch
        {
            PlaybackOutputBackendResult.Success => PlaybackOutputSubmitResult.Submitted,
            PlaybackOutputBackendResult.FormatUnsupported =>
                PlaybackOutputSubmitResult.FormatUnsupported,
            PlaybackOutputBackendResult.DeviceLost => PlaybackOutputSubmitResult.DeviceLost,
            PlaybackOutputBackendResult.TemporarilyUnavailable =>
                PlaybackOutputSubmitResult.TemporarilyUnavailable,
            PlaybackOutputBackendResult.Rejected => PlaybackOutputSubmitResult.Rejected,
            (PlaybackOutputBackendResult)(-2) => PlaybackOutputSubmitResult.Cancelled,
            _ => PlaybackOutputSubmitResult.Failure,
        };

    private static PlaybackOutputFlushResult MapFlush(PlaybackOutputBackendResult result) =>
        result switch
        {
            PlaybackOutputBackendResult.Success => PlaybackOutputFlushResult.Flushed,
            PlaybackOutputBackendResult.DeviceLost => PlaybackOutputFlushResult.DeviceLost,
            PlaybackOutputBackendResult.TemporarilyUnavailable =>
                PlaybackOutputFlushResult.TemporarilyUnavailable,
            PlaybackOutputBackendResult.Rejected => PlaybackOutputFlushResult.Rejected,
            (PlaybackOutputBackendResult)(-2) => PlaybackOutputFlushResult.Cancelled,
            _ => PlaybackOutputFlushResult.Failure,
        };

    private static PlaybackOutputResetResult MapReset(PlaybackOutputBackendResult result) =>
        result switch
        {
            PlaybackOutputBackendResult.Success => PlaybackOutputResetResult.Reset,
            PlaybackOutputBackendResult.DeviceLost => PlaybackOutputResetResult.DeviceLost,
            PlaybackOutputBackendResult.TemporarilyUnavailable =>
                PlaybackOutputResetResult.TemporarilyUnavailable,
            PlaybackOutputBackendResult.Rejected => PlaybackOutputResetResult.Rejected,
            (PlaybackOutputBackendResult)(-2) => PlaybackOutputResetResult.Cancelled,
            _ => PlaybackOutputResetResult.Failure,
        };

    private enum EntryResult
    {
        Entered,
        Cancelled,
        Disposed,
    }
}
