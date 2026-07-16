namespace PeerCast.Nova.Playback;

public enum PlaybackDecodedFrameDiscontinuityKind
{
    None,
    DecoderReset,
}

public enum PlaybackDecodedAudioSampleFormat
{
    PcmSigned16Interleaved,
    PcmFloat32Interleaved,
}

public enum PlaybackDecodedVideoPixelFormat
{
    Bgra32,
}

/// <summary>
/// Canonical interleaved PCM frame. Payload memory is callback-scoped and must
/// not be retained after the sink callback completes.
/// </summary>
public readonly record struct PlaybackDecodedAudioFrame
{
    public const int MinimumSampleRate = 8_000;
    public const int MaximumSampleRate = 384_000;
    public const int MinimumChannelCount = 1;
    public const int MaximumChannelCount = 32;
    public const int MaximumSampleCount = 1_048_576;
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(10);

    private PlaybackDecodedAudioFrame(
        long sequence,
        TimeSpan timestamp,
        TimeSpan duration,
        PlaybackDecodedFrameDiscontinuityKind discontinuity,
        PlaybackDecodedAudioSampleFormat sampleFormat,
        int sampleRate,
        int channelCount,
        int sampleCount,
        ReadOnlyMemory<byte> payload)
    {
        Sequence = sequence;
        Timestamp = timestamp;
        Duration = duration;
        Discontinuity = discontinuity;
        SampleFormat = sampleFormat;
        SampleRate = sampleRate;
        ChannelCount = channelCount;
        SampleCount = sampleCount;
        Payload = payload;
    }

    public long Sequence { get; }

    public TimeSpan Timestamp { get; }

    public TimeSpan Duration { get; }

    public PlaybackDecodedFrameDiscontinuityKind Discontinuity { get; }

    public PlaybackDecodedAudioSampleFormat SampleFormat { get; }

    public int SampleRate { get; }

    public int ChannelCount { get; }

    public int SampleCount { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public static PlaybackDecodedAudioFrame Create(
        long sequence,
        TimeSpan timestamp,
        TimeSpan duration,
        PlaybackDecodedFrameDiscontinuityKind discontinuity,
        PlaybackDecodedAudioSampleFormat sampleFormat,
        int sampleRate,
        int channelCount,
        int sampleCount,
        ReadOnlyMemory<byte> payload)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        ValidateDuration(duration);

        if (!Enum.IsDefined(discontinuity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(discontinuity),
                discontinuity,
                "The decoded-frame discontinuity is not defined.");
        }

        if (!Enum.IsDefined(sampleFormat))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleFormat),
                sampleFormat,
                "The decoded audio sample format is not defined.");
        }

        if (sampleRate is < MinimumSampleRate or > MaximumSampleRate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate),
                sampleRate,
                $"The sample rate must be between {MinimumSampleRate} and {MaximumSampleRate}.");
        }

        if (channelCount is < MinimumChannelCount or > MaximumChannelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channelCount),
                channelCount,
                $"The channel count must be between {MinimumChannelCount} and {MaximumChannelCount}.");
        }

        if (sampleCount is < 1 or > MaximumSampleCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleCount),
                sampleCount,
                $"The sample count must be between 1 and {MaximumSampleCount}.");
        }

        var expectedByteCount = checked(
            (long)sampleCount *
            channelCount *
            GetBytesPerSample(sampleFormat));

        if (expectedByteCount > int.MaxValue ||
            payload.Length != expectedByteCount)
        {
            throw new ArgumentException(
                "The audio payload length does not match the canonical interleaved PCM layout.",
                nameof(payload));
        }

        return new PlaybackDecodedAudioFrame(
            sequence,
            timestamp,
            duration,
            discontinuity,
            sampleFormat,
            sampleRate,
            channelCount,
            sampleCount,
            payload);
    }

    private static int GetBytesPerSample(
        PlaybackDecodedAudioSampleFormat sampleFormat)
    {
        return sampleFormat switch
        {
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved => 2,
            PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved => 4,
            _ => throw new ArgumentOutOfRangeException(
                nameof(sampleFormat),
                sampleFormat,
                "The decoded audio sample format is not defined."),
        };
    }

    private static void ValidateDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || duration > MaximumDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                $"The frame duration must be greater than zero and no more than {MaximumDuration}.");
        }
    }
}

/// <summary>
/// Canonical packed BGRA32 video frame. Payload memory is callback-scoped and
/// must not be retained after the sink callback completes.
/// </summary>
public readonly record struct PlaybackDecodedVideoFrame
{
    public const int MaximumDimension = 16_384;
    public const int MaximumStride = 262_144;
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(10);

    private PlaybackDecodedVideoFrame(
        long sequence,
        TimeSpan timestamp,
        TimeSpan duration,
        PlaybackDecodedFrameDiscontinuityKind discontinuity,
        PlaybackDecodedVideoPixelFormat pixelFormat,
        int width,
        int height,
        int stride,
        ReadOnlyMemory<byte> payload)
    {
        Sequence = sequence;
        Timestamp = timestamp;
        Duration = duration;
        Discontinuity = discontinuity;
        PixelFormat = pixelFormat;
        Width = width;
        Height = height;
        Stride = stride;
        Payload = payload;
    }

    public long Sequence { get; }

    public TimeSpan Timestamp { get; }

    public TimeSpan Duration { get; }

    public PlaybackDecodedFrameDiscontinuityKind Discontinuity { get; }

    public PlaybackDecodedVideoPixelFormat PixelFormat { get; }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public static PlaybackDecodedVideoFrame Create(
        long sequence,
        TimeSpan timestamp,
        TimeSpan duration,
        PlaybackDecodedFrameDiscontinuityKind discontinuity,
        PlaybackDecodedVideoPixelFormat pixelFormat,
        int width,
        int height,
        int stride,
        ReadOnlyMemory<byte> payload)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        ValidateDuration(duration);

        if (!Enum.IsDefined(discontinuity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(discontinuity),
                discontinuity,
                "The decoded-frame discontinuity is not defined.");
        }

        if (!Enum.IsDefined(pixelFormat))
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelFormat),
                pixelFormat,
                "The decoded video pixel format is not defined.");
        }

        if (width is < 1 or > MaximumDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                $"The video width must be between 1 and {MaximumDimension}.");
        }

        if (height is < 1 or > MaximumDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                $"The video height must be between 1 and {MaximumDimension}.");
        }

        var minimumStride = checked(width * GetBytesPerPixel(pixelFormat));

        if (stride < minimumStride || stride > MaximumStride)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stride),
                stride,
                $"The video stride must be between {minimumStride} and {MaximumStride}.");
        }

        var expectedByteCount = checked((long)stride * height);

        if (expectedByteCount > int.MaxValue ||
            payload.Length != expectedByteCount)
        {
            throw new ArgumentException(
                "The video payload length does not match the canonical packed frame layout.",
                nameof(payload));
        }

        return new PlaybackDecodedVideoFrame(
            sequence,
            timestamp,
            duration,
            discontinuity,
            pixelFormat,
            width,
            height,
            stride,
            payload);
    }

    private static int GetBytesPerPixel(
        PlaybackDecodedVideoPixelFormat pixelFormat)
    {
        return pixelFormat switch
        {
            PlaybackDecodedVideoPixelFormat.Bgra32 => 4,
            _ => throw new ArgumentOutOfRangeException(
                nameof(pixelFormat),
                pixelFormat,
                "The decoded video pixel format is not defined."),
        };
    }

    private static void ValidateDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || duration > MaximumDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                $"The frame duration must be greater than zero and no more than {MaximumDuration}.");
        }
    }
}
