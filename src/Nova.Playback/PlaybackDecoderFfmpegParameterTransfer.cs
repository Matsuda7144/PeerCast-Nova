using System.Runtime.InteropServices;

namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderFfmpegParameterTransferResult
{
    Applied,
    Unsupported,
    CodecMismatch,
    ContextUnavailable,
    ParametersUnavailable,
    GateRejected,
    NativeAllocationFailed,
    NativeTransferRejected,
    NativeFailure,
}

internal enum PlaybackDecoderFfmpegNativeParameterTransferResult
{
    Applied,
    AllocationFailed,
    TransferRejected,
}

internal interface IPlaybackDecoderFfmpegParameterTransferNativeApi
{
    PlaybackDecoderFfmpegNativeParameterTransferResult Transfer(
        nint allocateParametersFunction,
        nint freeParametersFunction,
        nint allocateZeroedFunction,
        nint initializeChannelLayoutFunction,
        nint transferParametersFunction,
        nint context,
        PlaybackDecoderFfmpegOwnedCodecParameters parameters);
}

public sealed class PlaybackDecoderFfmpegParameterTransfer
{
    private const string AllocateParametersSymbol = "avcodec_parameters_alloc";
    private const string FreeParametersSymbol = "avcodec_parameters_free";
    private const string AllocateZeroedSymbol = "av_mallocz";
    private const string InitializeChannelLayoutSymbol = "av_channel_layout_default";
    private const string TransferParametersSymbol = "avcodec_parameters_to_context";

    private readonly IPlaybackDecoderFfmpegParameterTransferNativeApi _nativeApi;
    private readonly bool _isSupportedPlatform;

    public PlaybackDecoderFfmpegParameterTransfer()
        : this(
            new WindowsPlaybackDecoderFfmpegParameterTransferNativeApi(),
            OperatingSystem.IsWindows() && Environment.Is64BitProcess)
    {
    }

    internal PlaybackDecoderFfmpegParameterTransfer(
        IPlaybackDecoderFfmpegParameterTransferNativeApi nativeApi,
        bool isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        _nativeApi = nativeApi;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackDecoderFfmpegParameterTransferResult Apply(
        PlaybackDecoderFfmpegContextLease contextLease,
        PlaybackDecoderFfmpegCodecParametersLease parametersLease)
    {
        ArgumentNullException.ThrowIfNull(contextLease);
        ArgumentNullException.ThrowIfNull(parametersLease);

        if (contextLease.CodecId != parametersLease.CodecId)
        {
            return PlaybackDecoderFfmpegParameterTransferResult.CodecMismatch;
        }

        if (!_isSupportedPlatform)
        {
            return PlaybackDecoderFfmpegParameterTransferResult.Unsupported;
        }

        if (contextLease.IsDisposed)
        {
            return PlaybackDecoderFfmpegParameterTransferResult.ContextUnavailable;
        }

        if (parametersLease.IsDisposed)
        {
            return PlaybackDecoderFfmpegParameterTransferResult.ParametersUnavailable;
        }

        PlaybackDecoderFfmpegParameterTransferResult result;
        try
        {
            result = contextLease.UseContextAndSymbols((context, symbols) =>
                parametersLease.UseOwnedParameters(parameters =>
                {
                    if (!symbols.TryGetValue(
                            AllocateParametersSymbol,
                            out var allocateParameters) ||
                        !symbols.TryGetValue(FreeParametersSymbol, out var freeParameters) ||
                        !symbols.TryGetValue(AllocateZeroedSymbol, out var allocateZeroed) ||
                        !symbols.TryGetValue(
                            InitializeChannelLayoutSymbol,
                            out var initializeChannelLayout) ||
                        !symbols.TryGetValue(
                            TransferParametersSymbol,
                            out var transferParameters))
                    {
                        return PlaybackDecoderFfmpegParameterTransferResult.GateRejected;
                    }

                    return _nativeApi.Transfer(
                        allocateParameters,
                        freeParameters,
                        allocateZeroed,
                        initializeChannelLayout,
                        transferParameters,
                        context,
                        parameters) switch
                    {
                        PlaybackDecoderFfmpegNativeParameterTransferResult.Applied =>
                            PlaybackDecoderFfmpegParameterTransferResult.Applied,
                        PlaybackDecoderFfmpegNativeParameterTransferResult.AllocationFailed =>
                            PlaybackDecoderFfmpegParameterTransferResult.NativeAllocationFailed,
                        PlaybackDecoderFfmpegNativeParameterTransferResult.TransferRejected =>
                            PlaybackDecoderFfmpegParameterTransferResult.NativeTransferRejected,
                        _ => PlaybackDecoderFfmpegParameterTransferResult.NativeFailure,
                    };
                }));
        }
        catch (ObjectDisposedException)
        {
            return parametersLease.IsDisposed
                ? PlaybackDecoderFfmpegParameterTransferResult.ParametersUnavailable
                : PlaybackDecoderFfmpegParameterTransferResult.ContextUnavailable;
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            NotSupportedException or
            OverflowException or
            ArgumentException)
        {
            contextLease.Dispose();
            return PlaybackDecoderFfmpegParameterTransferResult.NativeFailure;
        }

        if (result == PlaybackDecoderFfmpegParameterTransferResult.Applied)
        {
            parametersLease.Dispose();
        }
        else if (result is
            PlaybackDecoderFfmpegParameterTransferResult.NativeTransferRejected or
            PlaybackDecoderFfmpegParameterTransferResult.NativeFailure)
        {
            contextLease.Dispose();
        }

        return result;
    }
}

internal sealed class WindowsPlaybackDecoderFfmpegParameterTransferNativeApi :
    IPlaybackDecoderFfmpegParameterTransferNativeApi
{
    private const int CodecTypeOffset = 0;
    private const int CodecIdOffset = 4;
    private const int ExtradataOffset = 16;
    private const int ExtradataSizeOffset = 24;
    private const int WidthOffset = 72;
    private const int HeightOffset = 76;
    private const int ChannelLayoutOffset = 128;
    private const int SampleRateOffset = 152;
    private const int VideoMediaType = 0;
    private const int AudioMediaType = 1;

    public PlaybackDecoderFfmpegNativeParameterTransferResult Transfer(
        nint allocateParametersFunction,
        nint freeParametersFunction,
        nint allocateZeroedFunction,
        nint initializeChannelLayoutFunction,
        nint transferParametersFunction,
        nint context,
        PlaybackDecoderFfmpegOwnedCodecParameters parameters)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
        {
            throw new NotSupportedException("The native parameter boundary is unavailable.");
        }

        var allocateParameters =
            Marshal.GetDelegateForFunctionPointer<AllocateParametersFunction>(
                allocateParametersFunction);
        var freeParameters = Marshal.GetDelegateForFunctionPointer<FreeParametersFunction>(
            freeParametersFunction);
        var allocateZeroed = Marshal.GetDelegateForFunctionPointer<AllocateZeroedFunction>(
            allocateZeroedFunction);
        var initializeChannelLayout =
            Marshal.GetDelegateForFunctionPointer<InitializeChannelLayoutFunction>(
                initializeChannelLayoutFunction);
        var transferParameters =
            Marshal.GetDelegateForFunctionPointer<TransferParametersFunction>(
                transferParametersFunction);

        var nativeParameters = allocateParameters();
        if (nativeParameters == 0)
        {
            return PlaybackDecoderFfmpegNativeParameterTransferResult.AllocationFailed;
        }

        try
        {
            var nativeExtradata = allocateZeroed(
                checked((nuint)parameters.PaddedExtradata.Length));
            if (nativeExtradata == 0)
            {
                return PlaybackDecoderFfmpegNativeParameterTransferResult.AllocationFailed;
            }

            Marshal.WriteIntPtr(nativeParameters, ExtradataOffset, nativeExtradata);
            Marshal.WriteInt32(
                nativeParameters,
                ExtradataSizeOffset,
                parameters.ExtradataLength);

            var copy = parameters.PaddedExtradata.ToArray();
            try
            {
                Marshal.Copy(copy, 0, nativeExtradata, copy.Length);
            }
            finally
            {
                Array.Clear(copy, 0, copy.Length);
            }

            Marshal.WriteInt32(nativeParameters, CodecIdOffset, (int)parameters.CodecId);
            switch (parameters.CodecId)
            {
                case PlaybackDecoderFfmpegCodecId.H264:
                    Marshal.WriteInt32(nativeParameters, CodecTypeOffset, VideoMediaType);
                    Marshal.WriteInt32(nativeParameters, WidthOffset, parameters.Width);
                    Marshal.WriteInt32(nativeParameters, HeightOffset, parameters.Height);
                    break;

                case PlaybackDecoderFfmpegCodecId.Aac:
                    Marshal.WriteInt32(nativeParameters, CodecTypeOffset, AudioMediaType);
                    initializeChannelLayout(
                        nativeParameters + ChannelLayoutOffset,
                        parameters.ChannelCount);
                    Marshal.WriteInt32(
                        nativeParameters,
                        SampleRateOffset,
                        parameters.SampleRate);
                    break;

                default:
                    throw new ArgumentException(
                        "The native codec parameter category is undefined.",
                        nameof(parameters));
            }

            return transferParameters(context, nativeParameters) >= 0
                ? PlaybackDecoderFfmpegNativeParameterTransferResult.Applied
                : PlaybackDecoderFfmpegNativeParameterTransferResult.TransferRejected;
        }
        finally
        {
            freeParameters(ref nativeParameters);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint AllocateParametersFunction();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeParametersFunction(ref nint parameters);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint AllocateZeroedFunction(nuint size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void InitializeChannelLayoutFunction(nint layout, int channelCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int TransferParametersFunction(nint context, nint parameters);
}
