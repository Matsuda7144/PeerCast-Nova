using System.Runtime.InteropServices;

namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult
{
    Configured,
    Unsupported,
    InvalidTimeBase,
    ContextUnavailable,
    ParametersNotTransferred,
    AlreadyConfigured,
    ContextAlreadyOpened,
    GateRejected,
    NativeRejected,
    NativeFailure,
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct PlaybackDecoderFfmpegAvRational(
    int Numerator,
    int Denominator);

internal interface IPlaybackDecoderFfmpegPacketTimeBaseNativeApi
{
    int SetRational(
        nint function,
        nint context,
        string optionName,
        PlaybackDecoderFfmpegAvRational value,
        int flags);
}

public sealed class PlaybackDecoderFfmpegPacketTimeBaseConfigurationBoundary
{
    private const string SetRationalSymbol = "av_opt_set_q";
    private const string PacketTimeBaseOption = "pkt_timebase";
    private readonly IPlaybackDecoderFfmpegPacketTimeBaseNativeApi _nativeApi;
    private readonly bool _isSupportedPlatform;

    public PlaybackDecoderFfmpegPacketTimeBaseConfigurationBoundary()
        : this(
            new WindowsPlaybackDecoderFfmpegPacketTimeBaseNativeApi(),
            OperatingSystem.IsWindows() && Environment.Is64BitProcess)
    {
    }

    internal PlaybackDecoderFfmpegPacketTimeBaseConfigurationBoundary(
        IPlaybackDecoderFfmpegPacketTimeBaseNativeApi nativeApi,
        bool isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        _nativeApi = nativeApi;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult Configure(
        PlaybackDecoderFfmpegContextLease contextLease,
        PlaybackBoundedDemuxTimeBase timeBase)
    {
        ArgumentNullException.ThrowIfNull(contextLease);
        if (!PlaybackBoundedDemuxSession.IsValidTimeBase(timeBase))
        {
            return PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.InvalidTimeBase;
        }

        if (!_isSupportedPlatform)
        {
            return PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.Unsupported;
        }

        if (contextLease.IsDisposed)
        {
            return PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.ContextUnavailable;
        }

        try
        {
            var result = contextLease.UseContextAndSymbols((context, symbols) =>
            {
                if (contextLease.IsOpened)
                {
                    return PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult
                        .ContextAlreadyOpened;
                }

                if (!contextLease.HasTransferredParameters)
                {
                    return PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult
                        .ParametersNotTransferred;
                }

                if (contextLease.ConfiguredPacketTimeBase is not null)
                {
                    return PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult
                        .AlreadyConfigured;
                }

                if (!symbols.TryGetValue(SetRationalSymbol, out var setRational))
                {
                    return PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.GateRejected;
                }

                var nativeResult = _nativeApi.SetRational(
                    setRational,
                    context,
                    PacketTimeBaseOption,
                    new(timeBase.Numerator, timeBase.Denominator),
                    flags: 0);
                if (nativeResult < 0)
                {
                    return PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.NativeRejected;
                }

                contextLease.MarkPacketTimeBaseConfigured(timeBase);
                return PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.Configured;
            });
            if (result == PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.NativeRejected)
            {
                contextLease.Dispose();
            }

            return result;
        }
        catch (ObjectDisposedException)
        {
            return PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.ContextUnavailable;
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            NotSupportedException or
            ArgumentException or
            OverflowException)
        {
            contextLease.Dispose();
            return PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult.NativeFailure;
        }
    }
}

internal sealed class WindowsPlaybackDecoderFfmpegPacketTimeBaseNativeApi :
    IPlaybackDecoderFfmpegPacketTimeBaseNativeApi
{
    public int SetRational(
        nint function,
        nint context,
        string optionName,
        PlaybackDecoderFfmpegAvRational value,
        int flags)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
        {
            throw new NotSupportedException("The native packet-time-base boundary is unavailable.");
        }

        return Marshal.GetDelegateForFunctionPointer<SetRationalFunction>(function)(
            context,
            optionName,
            value,
            flags);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetRationalFunction(
        nint context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string optionName,
        PlaybackDecoderFfmpegAvRational value,
        int flags);
}
