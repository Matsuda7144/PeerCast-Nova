namespace PeerCast.Nova.Playback;

public enum PlaybackWindowsOutputBindingState
{
    Unbound,
    Bound,
    DeviceLost,
    Disposed,
}

public enum PlaybackWindowsOutputBindingResult
{
    Bound,
    ActivationRequired,
    PlatformUnsupported,
    FormatUnsupported,
    AlreadyBound,
    DeviceLost,
    InvalidEnvironment,
    Cancelled,
    Disposed,
    Failure,
}

public enum PlaybackWindowsOutputBindingResetResult
{
    Reset,
    NotBound,
    DeviceLost,
    Cancelled,
    Disposed,
    Failure,
}

public readonly record struct PlaybackWindowsOutputBindingSnapshot(
    PlaybackWindowsOutputBindingState VideoState,
    PlaybackWindowsOutputBindingState AudioState,
    long VideoGeneration,
    long AudioGeneration);

public sealed class PlaybackWindowsOutputResourceBindingBoundary : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PlaybackWindowsOutputActivationBoundary _activation;
    private readonly IPlaybackWindowsOutputNativeResourceBinding _native;
    private readonly Func<bool> _isSupportedPlatform;
    private PlaybackWindowsOutputBindingState _videoState;
    private PlaybackWindowsOutputBindingState _audioState;
    private long _videoGeneration;
    private long _audioGeneration;
    private bool _videoNativeBound;
    private bool _audioNativeBound;
    private volatile bool _isDisposed;

    public PlaybackWindowsOutputResourceBindingBoundary(
        PlaybackWindowsOutputActivationBoundary activation)
        : this(activation, new SystemPlaybackWindowsOutputNativeResourceBinding(),
            static () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000))
    {
    }

    internal PlaybackWindowsOutputResourceBindingBoundary(
        PlaybackWindowsOutputActivationBoundary activation,
        IPlaybackWindowsOutputNativeResourceBinding native,
        Func<bool> isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(isSupportedPlatform);
        _activation = activation;
        _native = native;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackWindowsOutputBindingSnapshot Snapshot => new(
        _videoState,
        _audioState,
        _videoGeneration,
        _audioGeneration);

    public async ValueTask<PlaybackWindowsOutputBindingResult> ConfigureAsync(
        PlaybackVideoRendererConfiguration video,
        PlaybackAudioDeviceConfiguration audio,
        CancellationToken cancellationToken)
    {
        if (_isDisposed)
        {
            return PlaybackWindowsOutputBindingResult.Disposed;
        }

        if (!IsSupported(video, audio))
        {
            return PlaybackWindowsOutputBindingResult.FormatUnsupported;
        }

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return PlaybackWindowsOutputBindingResult.Cancelled;
        }

        try
        {
            if (_isDisposed)
            {
                return PlaybackWindowsOutputBindingResult.Disposed;
            }

            if (!_isSupportedPlatform())
            {
                return PlaybackWindowsOutputBindingResult.PlatformUnsupported;
            }

            if (_videoState == PlaybackWindowsOutputBindingState.Bound ||
                _audioState == PlaybackWindowsOutputBindingState.Bound)
            {
                return PlaybackWindowsOutputBindingResult.AlreadyBound;
            }

            var borrow = _activation.BorrowActivatedResources(resources =>
                BindBorrowed(resources, video, audio));
            return MapBorrow(borrow);
        }
        catch
        {
            RollbackBindings();
            return PlaybackWindowsOutputBindingResult.Failure;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<PlaybackWindowsOutputBindingResetResult> ResetAsync(
        long generation,
        CancellationToken cancellationToken)
    {
        if (_isDisposed)
        {
            return PlaybackWindowsOutputBindingResetResult.Disposed;
        }

        if (generation < 0)
        {
            return PlaybackWindowsOutputBindingResetResult.Failure;
        }

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return PlaybackWindowsOutputBindingResetResult.Cancelled;
        }

        try
        {
            if (_isDisposed)
            {
                return PlaybackWindowsOutputBindingResetResult.Disposed;
            }

            if (_videoState == PlaybackWindowsOutputBindingState.Unbound &&
                _audioState == PlaybackWindowsOutputBindingState.Unbound)
            {
                return PlaybackWindowsOutputBindingResetResult.NotBound;
            }

            var deviceLost = _videoState == PlaybackWindowsOutputBindingState.DeviceLost ||
                _audioState == PlaybackWindowsOutputBindingState.DeviceLost;
            RollbackBindings();
            _videoGeneration = generation;
            _audioGeneration = generation;
            return deviceLost
                ? PlaybackWindowsOutputBindingResetResult.DeviceLost
                : PlaybackWindowsOutputBindingResetResult.Reset;
        }
        catch
        {
            return PlaybackWindowsOutputBindingResetResult.Failure;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _gate.Wait();
        try
        {
            RollbackBindings();
            _videoState = PlaybackWindowsOutputBindingState.Disposed;
            _audioState = PlaybackWindowsOutputBindingState.Disposed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private PlaybackWindowsOutputResourceBorrowResult BindBorrowed(
        PlaybackWindowsOutputBorrowedResources resources,
        PlaybackVideoRendererConfiguration video,
        PlaybackAudioDeviceConfiguration audio)
    {
        PlaybackWindowsOutputNativeBindingResult videoResult;
        try
        {
            videoResult = _native.BindVideo(resources.Direct3D11, video);
        }
        catch
        {
            TryUnbind(_native.UnbindVideo);
            return PlaybackWindowsOutputResourceBorrowResult.Failure;
        }
        if (!Enum.IsDefined(videoResult))
        {
            return PlaybackWindowsOutputResourceBorrowResult.Failure;
        }

        if (videoResult != PlaybackWindowsOutputNativeBindingResult.Bound)
        {
            TryUnbind(_native.UnbindVideo);
            return MapNative(videoResult);
        }

        _videoState = PlaybackWindowsOutputBindingState.Bound;
        _videoNativeBound = true;
        _videoGeneration = video.Generation;
        PlaybackWindowsOutputNativeBindingResult audioResult;
        try
        {
            audioResult = _native.BindAudio(resources.Wasapi, audio);
        }
        catch
        {
            TryUnbind(_native.UnbindAudio);
            RollbackBindings();
            return PlaybackWindowsOutputResourceBorrowResult.Failure;
        }
        if (!Enum.IsDefined(audioResult) ||
            audioResult != PlaybackWindowsOutputNativeBindingResult.Bound)
        {
            TryUnbind(_native.UnbindAudio);
            RollbackBindings();
            return Enum.IsDefined(audioResult)
                ? MapNative(audioResult)
                : PlaybackWindowsOutputResourceBorrowResult.Failure;
        }

        _audioState = PlaybackWindowsOutputBindingState.Bound;
        _audioNativeBound = true;
        _audioGeneration = audio.Generation;
        return PlaybackWindowsOutputResourceBorrowResult.Success;
    }

    private void RollbackBindings()
    {
        if (_audioNativeBound)
        {
            TryUnbind(_native.UnbindAudio);
            _audioNativeBound = false;
        }

        if (_videoNativeBound)
        {
            TryUnbind(_native.UnbindVideo);
            _videoNativeBound = false;
        }

        _audioState = PlaybackWindowsOutputBindingState.Unbound;
        _videoState = PlaybackWindowsOutputBindingState.Unbound;
    }

    private static void TryUnbind(Action unbind)
    {
        try
        {
            unbind();
        }
        catch
        {
            // Cleanup stays fixed-result and never transfers resource ownership.
        }
    }

    private static bool IsSupported(
        PlaybackVideoRendererConfiguration video,
        PlaybackAudioDeviceConfiguration audio) =>
        video.PixelFormat == PlaybackDecodedVideoPixelFormat.Bgra32 &&
        video.Width > 0 &&
        video.Height > 0 &&
        video.Stride >= video.Width * 4L &&
        video.Generation >= 0 &&
        audio.SampleFormat is PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved or
            PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved &&
        audio.SampleRate > 0 &&
        audio.ChannelCount > 0 &&
        audio.Generation >= 0;

    private PlaybackWindowsOutputBindingResult MapBorrow(
        PlaybackWindowsOutputResourceBorrowResult result)
    {
        if (result == PlaybackWindowsOutputResourceBorrowResult.DeviceLost)
        {
            _videoState = PlaybackWindowsOutputBindingState.DeviceLost;
            _audioState = PlaybackWindowsOutputBindingState.DeviceLost;
        }

        return result switch
        {
            PlaybackWindowsOutputResourceBorrowResult.Success =>
                PlaybackWindowsOutputBindingResult.Bound,
            PlaybackWindowsOutputResourceBorrowResult.ActivationRequired =>
                PlaybackWindowsOutputBindingResult.ActivationRequired,
            PlaybackWindowsOutputResourceBorrowResult.DeviceLost =>
                PlaybackWindowsOutputBindingResult.DeviceLost,
            PlaybackWindowsOutputResourceBorrowResult.InvalidEnvironment =>
                PlaybackWindowsOutputBindingResult.InvalidEnvironment,
            PlaybackWindowsOutputResourceBorrowResult.Disposed =>
                PlaybackWindowsOutputBindingResult.ActivationRequired,
            _ => PlaybackWindowsOutputBindingResult.Failure,
        };
    }

    private static PlaybackWindowsOutputResourceBorrowResult MapNative(
        PlaybackWindowsOutputNativeBindingResult result) => result switch
        {
            PlaybackWindowsOutputNativeBindingResult.Bound =>
                PlaybackWindowsOutputResourceBorrowResult.Success,
            PlaybackWindowsOutputNativeBindingResult.DeviceLost =>
                PlaybackWindowsOutputResourceBorrowResult.DeviceLost,
            _ => PlaybackWindowsOutputResourceBorrowResult.Failure,
        };
}

internal enum PlaybackWindowsOutputNativeBindingResult
{
    Bound,
    DeviceLost,
    Failure,
}

internal interface IPlaybackWindowsOutputNativeResourceBinding
{
    PlaybackWindowsOutputNativeBindingResult BindVideo(
        IDisposable borrowedResource,
        PlaybackVideoRendererConfiguration configuration);
    PlaybackWindowsOutputNativeBindingResult BindAudio(
        IDisposable borrowedResource,
        PlaybackAudioDeviceConfiguration configuration);
    void UnbindVideo();
    void UnbindAudio();
}

internal sealed class SystemPlaybackWindowsOutputNativeResourceBinding :
    IPlaybackWindowsOutputNativeResourceBinding
{
    public PlaybackWindowsOutputNativeBindingResult BindVideo(
        IDisposable borrowedResource,
        PlaybackVideoRendererConfiguration configuration) =>
        borrowedResource is null
            ? PlaybackWindowsOutputNativeBindingResult.Failure
            : PlaybackWindowsOutputNativeBindingResult.Bound;

    public PlaybackWindowsOutputNativeBindingResult BindAudio(
        IDisposable borrowedResource,
        PlaybackAudioDeviceConfiguration configuration) =>
        borrowedResource is null
            ? PlaybackWindowsOutputNativeBindingResult.Failure
            : PlaybackWindowsOutputNativeBindingResult.Bound;

    public void UnbindVideo() { }
    public void UnbindAudio() { }
}
