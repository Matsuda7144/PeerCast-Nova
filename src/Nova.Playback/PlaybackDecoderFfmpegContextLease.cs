using System.Runtime.InteropServices;

namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderFfmpegCodecId
{
    H264 = 27,
    Aac = 86018,
}

public enum PlaybackDecoderFfmpegContextLeaseResult
{
    Created,
    Unsupported,
    UnknownCodecId,
    ParentUnavailable,
    GateRejected,
    DecoderUnavailable,
    ContextAllocationFailed,
    NativeFailure,
}

public sealed class PlaybackDecoderFfmpegContextLeaseCreation
{
    internal PlaybackDecoderFfmpegContextLeaseCreation(
        PlaybackDecoderFfmpegContextLeaseResult result,
        PlaybackDecoderFfmpegContextLease? lease = null)
    {
        if (!Enum.IsDefined(result))
        {
            throw new ArgumentException(
                "The native context result category is undefined.",
                nameof(result));
        }

        if ((result == PlaybackDecoderFfmpegContextLeaseResult.Created) !=
            (lease is not null))
        {
            throw new ArgumentException(
                "The native context result is inconsistent.",
                nameof(lease));
        }

        Result = result;
        Lease = lease;
    }

    public PlaybackDecoderFfmpegContextLeaseResult Result { get; }

    public PlaybackDecoderFfmpegContextLease? Lease { get; }
}

internal interface IPlaybackDecoderFfmpegContextNativeApi
{
    nint FindDecoder(nint function, PlaybackDecoderFfmpegCodecId codecId);

    nint AllocateContext(nint function, nint decoder);

    void FreeContext(nint function, ref nint context);
}

public sealed class PlaybackDecoderFfmpegContextLeaseFactory
{
    private const string FindDecoderSymbol = "avcodec_find_decoder";
    private const string AllocateContextSymbol = "avcodec_alloc_context3";
    private const string FreeContextSymbol = "avcodec_free_context";

    private readonly PlaybackDecoderFfmpegAbiSymbolGate _gate;
    private readonly IPlaybackDecoderFfmpegContextNativeApi _nativeApi;
    private readonly bool _isSupportedPlatform;

    public PlaybackDecoderFfmpegContextLeaseFactory()
        : this(
            new PlaybackDecoderFfmpegAbiSymbolGate(),
            new WindowsPlaybackDecoderFfmpegContextNativeApi(),
            OperatingSystem.IsWindows())
    {
    }

    internal PlaybackDecoderFfmpegContextLeaseFactory(
        PlaybackDecoderFfmpegAbiSymbolGate gate,
        IPlaybackDecoderFfmpegContextNativeApi nativeApi,
        bool isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(nativeApi);
        _gate = gate;
        _nativeApi = nativeApi;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackDecoderFfmpegContextLeaseCreation Create(
        VerifiedPlaybackDecoderRuntimeLease runtimeLease,
        PlaybackDecoderFfmpegCodecId codecId)
        => Create(runtimeLease, codecId, ownsRuntimeLease: true);

    internal PlaybackDecoderFfmpegContextLeaseCreation CreateSharingRuntime(
        VerifiedPlaybackDecoderRuntimeLease runtimeLease,
        PlaybackDecoderFfmpegCodecId codecId)
        => Create(runtimeLease, codecId, ownsRuntimeLease: false);

    private PlaybackDecoderFfmpegContextLeaseCreation Create(
        VerifiedPlaybackDecoderRuntimeLease runtimeLease,
        PlaybackDecoderFfmpegCodecId codecId,
        bool ownsRuntimeLease)
    {
        ArgumentNullException.ThrowIfNull(runtimeLease);
        if (!Enum.IsDefined(codecId))
        {
            return new(PlaybackDecoderFfmpegContextLeaseResult.UnknownCodecId);
        }

        if (!_isSupportedPlatform)
        {
            return new(PlaybackDecoderFfmpegContextLeaseResult.Unsupported);
        }

        if (runtimeLease.IsDisposed)
        {
            return new(PlaybackDecoderFfmpegContextLeaseResult.ParentUnavailable);
        }

        var gateAcquisition = _gate.Acquire(runtimeLease);
        if (gateAcquisition.Lease is not { } gateLease)
        {
            return new(gateAcquisition.Result ==
                PlaybackDecoderFfmpegAbiSymbolGateResult.LeaseUnavailable
                    ? PlaybackDecoderFfmpegContextLeaseResult.ParentUnavailable
                    : PlaybackDecoderFfmpegContextLeaseResult.GateRejected);
        }

        return Create(gateLease, codecId, ownsRuntimeLease);
    }

    public PlaybackDecoderFfmpegContextLeaseCreation Create(
        PlaybackDecoderFfmpegAbiSymbolGateLease gateLease,
        PlaybackDecoderFfmpegCodecId codecId)
        => Create(gateLease, codecId, ownsRuntimeLease: true);

    private PlaybackDecoderFfmpegContextLeaseCreation Create(
        PlaybackDecoderFfmpegAbiSymbolGateLease gateLease,
        PlaybackDecoderFfmpegCodecId codecId,
        bool ownsRuntimeLease)
    {
        ArgumentNullException.ThrowIfNull(gateLease);
        if (!Enum.IsDefined(codecId))
        {
            return new(PlaybackDecoderFfmpegContextLeaseResult.UnknownCodecId);
        }

        if (!_isSupportedPlatform)
        {
            return new(PlaybackDecoderFfmpegContextLeaseResult.Unsupported);
        }

        var runtimeLease = gateLease.RuntimeLease;
        if (gateLease.IsDisposed || runtimeLease.IsDisposed)
        {
            return new(PlaybackDecoderFfmpegContextLeaseResult.ParentUnavailable);
        }

        var nativeResult = PlaybackDecoderFfmpegContextLeaseResult.Created;
        nint context = 0;
        nint freeContext = 0;
        try
        {
            gateLease.UseSymbols(symbols =>
            {
                if (!symbols.TryGetValue(FindDecoderSymbol, out var findDecoder) ||
                    !symbols.TryGetValue(AllocateContextSymbol, out var allocateContext) ||
                    !symbols.TryGetValue(FreeContextSymbol, out freeContext))
                {
                    nativeResult = PlaybackDecoderFfmpegContextLeaseResult.GateRejected;
                    return false;
                }

                var decoder = _nativeApi.FindDecoder(findDecoder, codecId);
                if (decoder == 0)
                {
                    nativeResult = PlaybackDecoderFfmpegContextLeaseResult.DecoderUnavailable;
                    return false;
                }

                context = _nativeApi.AllocateContext(allocateContext, decoder);
                if (context == 0)
                {
                    nativeResult =
                        PlaybackDecoderFfmpegContextLeaseResult.ContextAllocationFailed;
                    return false;
                }

                return true;
            });
        }
        catch (ObjectDisposedException)
        {
            nativeResult = PlaybackDecoderFfmpegContextLeaseResult.ParentUnavailable;
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            NotSupportedException)
        {
            nativeResult = PlaybackDecoderFfmpegContextLeaseResult.NativeFailure;
        }

        if (nativeResult != PlaybackDecoderFfmpegContextLeaseResult.Created)
        {
            gateLease.Dispose();
            return new(nativeResult);
        }

        return new(
            PlaybackDecoderFfmpegContextLeaseResult.Created,
            new PlaybackDecoderFfmpegContextLease(
                runtimeLease,
                gateLease,
                _nativeApi,
                codecId,
                freeContext,
                context,
                ownsRuntimeLease));
    }
}

public sealed class PlaybackDecoderFfmpegContextLease : IDisposable, IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly VerifiedPlaybackDecoderRuntimeLease _runtimeLease;
    private readonly PlaybackDecoderFfmpegAbiSymbolGateLease _gateLease;
    private readonly IPlaybackDecoderFfmpegContextNativeApi _nativeApi;
    private readonly bool _ownsRuntimeLease;
    private readonly nint _freeContext;
    private nint _context;
    private bool _parametersTransferred;
    private PlaybackBoundedDemuxTimeBase? _configuredPacketTimeBase;
    private bool _isOpened;

    internal PlaybackDecoderFfmpegContextLease(
        VerifiedPlaybackDecoderRuntimeLease runtimeLease,
        PlaybackDecoderFfmpegAbiSymbolGateLease gateLease,
        IPlaybackDecoderFfmpegContextNativeApi nativeApi,
        PlaybackDecoderFfmpegCodecId codecId,
        nint freeContext,
        nint context,
        bool ownsRuntimeLease = true)
    {
        _runtimeLease = runtimeLease;
        _gateLease = gateLease;
        _nativeApi = nativeApi;
        _ownsRuntimeLease = ownsRuntimeLease;
        CodecId = codecId;
        _freeContext = freeContext;
        _context = context;
    }

    public PlaybackDecoderFfmpegCodecId CodecId { get; }

    public bool IsDisposed
    {
        get
        {
            lock (_sync)
            {
                return _context == 0;
            }
        }
    }

    internal bool HasTransferredParameters
    {
        get
        {
            lock (_sync)
            {
                return _context != 0 && _parametersTransferred;
            }
        }
    }

    internal bool IsOpened
    {
        get
        {
            lock (_sync)
            {
                return _context != 0 && _isOpened;
            }
        }
    }

    internal PlaybackBoundedDemuxTimeBase? ConfiguredPacketTimeBase
    {
        get
        {
            lock (_sync)
            {
                return _context != 0 ? _configuredPacketTimeBase : null;
            }
        }
    }

    internal TResult UseContext<TResult>(Func<nint, TResult> use)
    {
        ArgumentNullException.ThrowIfNull(use);
        lock (_sync)
        {
            EnsureAvailable();
            return use(_context);
        }
    }

    internal TResult UseContextAndSymbols<TResult>(
        Func<nint, IReadOnlyDictionary<string, nint>, TResult> use)
    {
        ArgumentNullException.ThrowIfNull(use);
        lock (_sync)
        {
            EnsureAvailable();
            return _gateLease.UseSymbols(symbols => use(_context, symbols));
        }
    }

    internal void MarkParametersTransferred()
    {
        lock (_sync)
        {
            EnsureAvailable();
            if (_parametersTransferred || _isOpened)
            {
                throw new InvalidOperationException(
                    "Codec parameters cannot be transferred after the context is configured.");
            }

            _parametersTransferred = true;
        }
    }

    internal void MarkOpened()
    {
        lock (_sync)
        {
            EnsureAvailable();
            if (!_parametersTransferred || _isOpened)
            {
                throw new InvalidOperationException(
                    "The native decoder context is not ready to be opened.");
            }

            _isOpened = true;
        }
    }

    internal void MarkPacketTimeBaseConfigured(PlaybackBoundedDemuxTimeBase timeBase)
    {
        lock (_sync)
        {
            EnsureAvailable();
            if (!PlaybackBoundedDemuxSession.IsValidTimeBase(timeBase) ||
                !_parametersTransferred ||
                _configuredPacketTimeBase is not null ||
                _isOpened)
            {
                throw new InvalidOperationException(
                    "The packet time base cannot be configured in the current context state.");
            }

            _configuredPacketTimeBase = timeBase;
        }
    }

    public void Dispose()
    {
        nint context;
        lock (_sync)
        {
            context = _context;
            _context = 0;
            _parametersTransferred = false;
            _configuredPacketTimeBase = null;
            _isOpened = false;
        }

        if (context == 0)
        {
            return;
        }

        try
        {
            if (!_gateLease.IsDisposed)
            {
                _nativeApi.FreeContext(_freeContext, ref context);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException)
        {
            // Cleanup remains privacy-safe and continues through every owner.
        }
        finally
        {
            try
            {
                _gateLease.Dispose();
            }
            finally
            {
                if (_ownsRuntimeLease)
                {
                    _runtimeLease.Dispose();
                }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void EnsureAvailable()
    {
        if (_context == 0 || _gateLease.IsDisposed || _runtimeLease.IsDisposed)
        {
            throw new ObjectDisposedException(
                nameof(PlaybackDecoderFfmpegContextLease),
                "The native decoder context lease is no longer available.");
        }
    }
}

internal sealed class WindowsPlaybackDecoderFfmpegContextNativeApi :
    IPlaybackDecoderFfmpegContextNativeApi
{
    public nint FindDecoder(nint function, PlaybackDecoderFfmpegCodecId codecId) =>
        Marshal.GetDelegateForFunctionPointer<FindDecoderFunction>(function)((int)codecId);

    public nint AllocateContext(nint function, nint decoder) =>
        Marshal.GetDelegateForFunctionPointer<AllocateContextFunction>(function)(decoder);

    public void FreeContext(nint function, ref nint context) =>
        Marshal.GetDelegateForFunctionPointer<FreeContextFunction>(function)(ref context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint FindDecoderFunction(int codecId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint AllocateContextFunction(nint decoder);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeContextFunction(ref nint context);
}
