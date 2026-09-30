namespace PeerCast.Nova.MediaEncoding;

public sealed record H264EncoderConfiguration
{
    public H264EncoderConfiguration(int width, int height, int framesPerSecond = 30,
        int bitRate = 300_000, int keyFrameInterval = 30)
    {
        if (width is < 16 or > 4096 || height is < 16 or > 4096 ||
            width % 2 != 0 || height % 2 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width),
                "YUV420P requires even dimensions between 16 and 4096.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(framesPerSecond, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(framesPerSecond, 120);
        ArgumentOutOfRangeException.ThrowIfLessThan(bitRate, 16_000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitRate, 50_000_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(keyFrameInterval, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(keyFrameInterval, 600);
        Width = width;
        Height = height;
        FramesPerSecond = framesPerSecond;
        BitRate = bitRate;
        KeyFrameInterval = keyFrameInterval;
    }

    public int Width { get; }
    public int Height { get; }
    public int FramesPerSecond { get; }
    public int BitRate { get; }
    public int KeyFrameInterval { get; }
}

/// <summary>The initial software audio profile is AAC-LC, 48 kHz stereo.</summary>
public sealed record AacEncoderConfiguration
{
    public AacEncoderConfiguration(int sampleRate = 48_000, int channelCount = 2,
        int bitRate = 128_000)
    {
        if (sampleRate != 48_000 || channelCount != 2)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate),
                "Only 48 kHz stereo AAC-LC is supported.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(bitRate, 32_000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitRate, 320_000);
        BitRate = bitRate;
        SampleRate = sampleRate;
        ChannelCount = channelCount;
    }

    public int SampleRate { get; }
    public int ChannelCount { get; }
    public int BitRate { get; }
}

/// <summary>Owned contiguous Y, U, V planes, without row padding; PTS is a frame index.</summary>
public sealed class RawYuv420VideoFrame
{
    private readonly byte[] _data;

    public RawYuv420VideoFrame(int width, int height, long presentationTimestamp,
        ReadOnlyMemory<byte> data)
    {
        _ = new H264EncoderConfiguration(width, height);
        ArgumentOutOfRangeException.ThrowIfNegative(presentationTimestamp);
        if (data.Length != checked(width * height * 3 / 2))
        {
            throw new ArgumentException("The YUV420P plane lengths are inconsistent.", nameof(data));
        }

        Width = width;
        Height = height;
        PresentationTimestamp = presentationTimestamp;
        _data = data.ToArray();
    }

    public int Width { get; }
    public int Height { get; }
    public long PresentationTimestamp { get; }
    public ReadOnlyMemory<byte> Data => _data.ToArray();
}

/// <summary>Owned finite interleaved float PCM; PTS counts stereo sample frames.</summary>
public sealed class RawPcmAudioFrame
{
    private readonly float[] _samples;

    public RawPcmAudioFrame(long presentationTimestamp, ReadOnlyMemory<float> samples)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(presentationTimestamp);
        if (samples.Length != 2048)
        {
            throw new ArgumentException("The initial AAC profile accepts 1024 stereo samples per input.",
                nameof(samples));
        }

        _samples = samples.ToArray();
        if (_samples.Any(static sample => !float.IsFinite(sample) || Math.Abs(sample) > 1))
        {
            throw new ArgumentException("PCM must contain finite normalized samples.", nameof(samples));
        }

        PresentationTimestamp = presentationTimestamp;
    }

    public long PresentationTimestamp { get; }
    public int SampleCount => _samples.Length / 2;
    public ReadOnlyMemory<float> Samples => _samples.ToArray();
}

/// <summary>
/// Owned compressed access unit. Times are in the session's explicit timebase;
/// AAC priming may produce negative DTS/PTS, which are preserved here.
/// </summary>
public sealed class MediaEncodedSample
{
    private readonly byte[] _payload;

    internal MediaEncodedSample(long dts, long pts, long duration, bool isKeyFrame,
        ReadOnlyMemory<byte> payload)
    {
        if (duration <= 0 || payload.IsEmpty || payload.Length > 4 * 1024 * 1024)
        {
            throw new ArgumentException("The encoded access unit is invalid.", nameof(payload));
        }

        DecodeTimestamp = dts;
        PresentationTimestamp = pts;
        Duration = duration;
        IsKeyFrame = isKeyFrame;
        _payload = payload.ToArray();
    }

    public long DecodeTimestamp { get; }
    public long PresentationTimestamp { get; }
    public long Duration { get; }
    public bool IsKeyFrame { get; }
    public ReadOnlyMemory<byte> Payload => _payload.ToArray();
}

public readonly record struct MediaEncoderSnapshot(
    long SubmittedFrameCount, long EncodedPacketCount, long FlushPacketCount, bool Completed);
