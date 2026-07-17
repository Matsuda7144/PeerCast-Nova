namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderFfmpegCodecParametersResult
{
    Created,
    UnknownCodecId,
    EmptyExtradata,
    ExtradataTooLarge,
    NumericValueOutOfRange,
    InconsistentParameters,
}

public readonly record struct PlaybackDecoderFfmpegCodecParametersInput
{
    public PlaybackDecoderFfmpegCodecParametersInput(
        PlaybackDecoderFfmpegCodecId codecId,
        int width,
        int height,
        int sampleRate,
        int channelCount,
        ReadOnlyMemory<byte> extradata)
    {
        CodecId = codecId;
        Width = width;
        Height = height;
        SampleRate = sampleRate;
        ChannelCount = channelCount;
        Extradata = extradata;
    }

    public PlaybackDecoderFfmpegCodecId CodecId { get; }

    public int Width { get; }

    public int Height { get; }

    public int SampleRate { get; }

    public int ChannelCount { get; }

    public ReadOnlyMemory<byte> Extradata { get; }

    public static PlaybackDecoderFfmpegCodecParametersInput ForH264(
        int width,
        int height,
        ReadOnlyMemory<byte> extradata) =>
        new(
            PlaybackDecoderFfmpegCodecId.H264,
            width,
            height,
            sampleRate: 0,
            channelCount: 0,
            extradata);

    public static PlaybackDecoderFfmpegCodecParametersInput ForAac(
        int sampleRate,
        int channelCount,
        ReadOnlyMemory<byte> extradata) =>
        new(
            PlaybackDecoderFfmpegCodecId.Aac,
            width: 0,
            height: 0,
            sampleRate,
            channelCount,
            extradata);
}

public sealed class PlaybackDecoderFfmpegCodecParametersCreation
{
    internal PlaybackDecoderFfmpegCodecParametersCreation(
        PlaybackDecoderFfmpegCodecParametersResult result,
        PlaybackDecoderFfmpegCodecParametersLease? lease = null)
    {
        if (!Enum.IsDefined(result))
        {
            throw new ArgumentException(
                "The codec-parameter result category is undefined.",
                nameof(result));
        }

        if ((result == PlaybackDecoderFfmpegCodecParametersResult.Created) !=
            (lease is not null))
        {
            throw new ArgumentException(
                "The codec-parameter result is inconsistent.",
                nameof(lease));
        }

        Result = result;
        Lease = lease;
    }

    public PlaybackDecoderFfmpegCodecParametersResult Result { get; }

    public PlaybackDecoderFfmpegCodecParametersLease? Lease { get; }
}

public static class PlaybackDecoderFfmpegCodecParametersFactory
{
    public const int FfmpegInputBufferPaddingSize = 64;
    public const int MaximumExtradataLength = 1_048_576;
    public const int MaximumVideoDimension = 16_384;
    public const long MaximumVideoPixelCount = 268_435_456;
    public const int MinimumAudioSampleRate = 8_000;
    public const int MaximumAudioSampleRate = 384_000;
    public const int MinimumAudioChannelCount = 1;
    public const int MaximumAudioChannelCount = 32;

    public static PlaybackDecoderFfmpegCodecParametersCreation Create(
        PlaybackDecoderFfmpegCodecParametersInput input)
    {
        if (!Enum.IsDefined(input.CodecId))
        {
            return new(PlaybackDecoderFfmpegCodecParametersResult.UnknownCodecId);
        }

        if (input.Extradata.IsEmpty)
        {
            return new(PlaybackDecoderFfmpegCodecParametersResult.EmptyExtradata);
        }

        if (input.Extradata.Length > MaximumExtradataLength)
        {
            return new(PlaybackDecoderFfmpegCodecParametersResult.ExtradataTooLarge);
        }

        var validation = ValidateNumericAndCodecSpecificFields(input);
        if (validation != PlaybackDecoderFfmpegCodecParametersResult.Created)
        {
            return new(validation);
        }

        int ownedLength;
        try
        {
            ownedLength = checked(input.Extradata.Length + FfmpegInputBufferPaddingSize);
        }
        catch (OverflowException)
        {
            return new(PlaybackDecoderFfmpegCodecParametersResult.NumericValueOutOfRange);
        }

        var ownedExtradata = new byte[ownedLength];
        input.Extradata.Span.CopyTo(ownedExtradata);
        ownedExtradata.AsSpan(input.Extradata.Length).Clear();

        return new(
            PlaybackDecoderFfmpegCodecParametersResult.Created,
            new PlaybackDecoderFfmpegCodecParametersLease(
                input.CodecId,
                input.Width,
                input.Height,
                input.SampleRate,
                input.ChannelCount,
                input.Extradata.Length,
                ownedExtradata));
    }

    private static PlaybackDecoderFfmpegCodecParametersResult
        ValidateNumericAndCodecSpecificFields(
            PlaybackDecoderFfmpegCodecParametersInput input)
    {
        if (input.Width < 0 ||
            input.Height < 0 ||
            input.SampleRate < 0 ||
            input.ChannelCount < 0)
        {
            return PlaybackDecoderFfmpegCodecParametersResult.NumericValueOutOfRange;
        }

        return input.CodecId switch
        {
            PlaybackDecoderFfmpegCodecId.H264 => ValidateH264(input),
            PlaybackDecoderFfmpegCodecId.Aac => ValidateAac(input),
            _ => PlaybackDecoderFfmpegCodecParametersResult.UnknownCodecId,
        };
    }

    private static PlaybackDecoderFfmpegCodecParametersResult ValidateH264(
        PlaybackDecoderFfmpegCodecParametersInput input)
    {
        if (input.SampleRate != 0 || input.ChannelCount != 0)
        {
            return PlaybackDecoderFfmpegCodecParametersResult.InconsistentParameters;
        }

        if (input.Width is < 1 or > MaximumVideoDimension ||
            input.Height is < 1 or > MaximumVideoDimension)
        {
            return PlaybackDecoderFfmpegCodecParametersResult.NumericValueOutOfRange;
        }

        var pixelCount = checked((long)input.Width * input.Height);
        return pixelCount <= MaximumVideoPixelCount
            ? PlaybackDecoderFfmpegCodecParametersResult.Created
            : PlaybackDecoderFfmpegCodecParametersResult.NumericValueOutOfRange;
    }

    private static PlaybackDecoderFfmpegCodecParametersResult ValidateAac(
        PlaybackDecoderFfmpegCodecParametersInput input)
    {
        if (input.Width != 0 || input.Height != 0)
        {
            return PlaybackDecoderFfmpegCodecParametersResult.InconsistentParameters;
        }

        if (input.SampleRate is < MinimumAudioSampleRate or > MaximumAudioSampleRate ||
            input.ChannelCount is < MinimumAudioChannelCount or > MaximumAudioChannelCount)
        {
            return PlaybackDecoderFfmpegCodecParametersResult.NumericValueOutOfRange;
        }

        return PlaybackDecoderFfmpegCodecParametersResult.Created;
    }
}

public sealed class PlaybackDecoderFfmpegCodecParametersLease : IDisposable, IAsyncDisposable
{
    private readonly object _sync = new();
    private byte[]? _paddedExtradata;

    internal PlaybackDecoderFfmpegCodecParametersLease(
        PlaybackDecoderFfmpegCodecId codecId,
        int width,
        int height,
        int sampleRate,
        int channelCount,
        int extradataLength,
        byte[] paddedExtradata)
    {
        CodecId = codecId;
        Width = width;
        Height = height;
        SampleRate = sampleRate;
        ChannelCount = channelCount;
        ExtradataLength = extradataLength;
        _paddedExtradata = paddedExtradata;
    }

    public PlaybackDecoderFfmpegCodecId CodecId { get; }

    public int Width { get; }

    public int Height { get; }

    public int SampleRate { get; }

    public int ChannelCount { get; }

    public int ExtradataLength { get; }

    public bool IsDisposed
    {
        get
        {
            lock (_sync)
            {
                return _paddedExtradata is null;
            }
        }
    }

    internal TResult UseOwnedParameters<TResult>(
        Func<PlaybackDecoderFfmpegOwnedCodecParameters, TResult> use)
    {
        ArgumentNullException.ThrowIfNull(use);
        lock (_sync)
        {
            var paddedExtradata = _paddedExtradata ??
                throw new ObjectDisposedException(
                    nameof(PlaybackDecoderFfmpegCodecParametersLease),
                    "The owned codec parameters are no longer available.");

            return use(new PlaybackDecoderFfmpegOwnedCodecParameters(
                CodecId,
                Width,
                Height,
                SampleRate,
                ChannelCount,
                ExtradataLength,
                paddedExtradata));
        }
    }

    public void Dispose()
    {
        byte[]? paddedExtradata;
        lock (_sync)
        {
            paddedExtradata = _paddedExtradata;
            _paddedExtradata = null;
        }

        if (paddedExtradata is null)
        {
            return;
        }

        Array.Clear(paddedExtradata, 0, paddedExtradata.Length);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

internal readonly record struct PlaybackDecoderFfmpegOwnedCodecParameters(
    PlaybackDecoderFfmpegCodecId CodecId,
    int Width,
    int Height,
    int SampleRate,
    int ChannelCount,
    int ExtradataLength,
    ReadOnlyMemory<byte> PaddedExtradata)
{
    public ReadOnlyMemory<byte> Extradata => PaddedExtradata[..ExtradataLength];

    public ReadOnlyMemory<byte> Padding => PaddedExtradata[ExtradataLength..];
}
