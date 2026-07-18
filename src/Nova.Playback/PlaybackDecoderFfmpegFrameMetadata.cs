using System.Runtime.InteropServices;

namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderFfmpegFrameMetadataResult
{
    Extracted,
    NeedMoreInput,
    EndOfStream,
    Unsupported,
    ContextUnavailable,
    ContextNotOpened,
    GateRejected,
    InvalidMetadata,
    ConsumerRejected,
    NativeFailure,
}

public enum PlaybackDecoderFfmpegFrameMediaKind
{
    Video,
    Audio,
}

public enum PlaybackDecoderFfmpegFrameFormat
{
    VideoYuv420P,
    VideoNv12,
    VideoBgra32,
    AudioSigned16,
    AudioFloat32,
    AudioSigned16Planar,
    AudioFloat32Planar,
}

public readonly record struct PlaybackDecoderFfmpegFrameTime
{
    private PlaybackDecoderFfmpegFrameTime(bool isKnown, TimeSpan value)
    {
        IsKnown = isKnown;
        Value = value;
    }

    public static PlaybackDecoderFfmpegFrameTime Unknown => default;

    public bool IsKnown { get; }

    public TimeSpan Value { get; }

    internal static PlaybackDecoderFfmpegFrameTime Known(TimeSpan value) => new(true, value);
}

public readonly record struct PlaybackDecoderFfmpegFrameMetadata
{
    internal PlaybackDecoderFfmpegFrameMetadata(
        PlaybackDecoderFfmpegFrameMediaKind mediaKind,
        PlaybackDecoderFfmpegFrameFormat format,
        int width,
        int height,
        int sampleRate,
        int channelCount,
        int sampleCount,
        PlaybackDecoderFfmpegFrameTime presentationTimestamp,
        PlaybackDecoderFfmpegFrameTime bestEffortTimestamp,
        PlaybackDecoderFfmpegFrameTime duration)
    {
        MediaKind = mediaKind;
        Format = format;
        Width = width;
        Height = height;
        SampleRate = sampleRate;
        ChannelCount = channelCount;
        SampleCount = sampleCount;
        PresentationTimestamp = presentationTimestamp;
        BestEffortTimestamp = bestEffortTimestamp;
        Duration = duration;
    }

    public PlaybackDecoderFfmpegFrameMediaKind MediaKind { get; }
    public PlaybackDecoderFfmpegFrameFormat Format { get; }
    public int Width { get; }
    public int Height { get; }
    public int SampleRate { get; }
    public int ChannelCount { get; }
    public int SampleCount { get; }
    public PlaybackDecoderFfmpegFrameTime PresentationTimestamp { get; }
    public PlaybackDecoderFfmpegFrameTime BestEffortTimestamp { get; }
    public PlaybackDecoderFfmpegFrameTime Duration { get; }
}

public interface IPlaybackDecoderFfmpegFrameMetadataConsumer
{
    bool Consume(PlaybackDecoderFfmpegFrameMetadata metadata);
}

internal readonly record struct PlaybackDecoderFfmpegRawFrameMetadata(
    int Format,
    int Width,
    int Height,
    int SampleRate,
    int ChannelCount,
    int SampleCount,
    long PresentationTimestamp,
    long BestEffortTimestamp,
    long Duration,
    int TimeBaseNumerator,
    int TimeBaseDenominator);

internal interface IPlaybackDecoderFfmpegFrameMetadataNativeApi
{
    PlaybackDecoderFfmpegRawFrameMetadata Read(nint frame);
}

public sealed class PlaybackDecoderFfmpegFrameMetadataBoundary
{
    private const long NoTimestamp = long.MinValue;
    private readonly PlaybackDecoderFfmpegNativeFrameBoundary _frameBoundary;
    private readonly IPlaybackDecoderFfmpegFrameMetadataNativeApi _nativeApi;
    private readonly bool _isSupportedPlatform;

    public PlaybackDecoderFfmpegFrameMetadataBoundary()
        : this(
            new PlaybackDecoderFfmpegNativeFrameBoundary(),
            new WindowsFfmpeg81FrameMetadataNativeApi(),
            OperatingSystem.IsWindows() && Environment.Is64BitProcess)
    {
    }

    internal PlaybackDecoderFfmpegFrameMetadataBoundary(
        PlaybackDecoderFfmpegNativeFrameBoundary frameBoundary,
        IPlaybackDecoderFfmpegFrameMetadataNativeApi nativeApi,
        bool isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(frameBoundary);
        ArgumentNullException.ThrowIfNull(nativeApi);
        _frameBoundary = frameBoundary;
        _nativeApi = nativeApi;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackDecoderFfmpegFrameMetadataResult ReceiveAndExtract(
        PlaybackDecoderFfmpegContextLease contextLease,
        IPlaybackDecoderFfmpegFrameMetadataConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(contextLease);
        ArgumentNullException.ThrowIfNull(consumer);
        if (!_isSupportedPlatform)
        {
            return PlaybackDecoderFfmpegFrameMetadataResult.Unsupported;
        }

        var adapter = new MetadataConsumerAdapter(_nativeApi, contextLease.CodecId, consumer);
        var frameResult = _frameBoundary.ReceiveAndProcess(contextLease, adapter);
        if (adapter.ValidationFailed)
        {
            return PlaybackDecoderFfmpegFrameMetadataResult.InvalidMetadata;
        }

        return frameResult switch
        {
            PlaybackDecoderFfmpegNativeFrameResult.Processed =>
                PlaybackDecoderFfmpegFrameMetadataResult.Extracted,
            PlaybackDecoderFfmpegNativeFrameResult.NeedMoreInput =>
                PlaybackDecoderFfmpegFrameMetadataResult.NeedMoreInput,
            PlaybackDecoderFfmpegNativeFrameResult.EndOfStream =>
                PlaybackDecoderFfmpegFrameMetadataResult.EndOfStream,
            PlaybackDecoderFfmpegNativeFrameResult.Unsupported =>
                PlaybackDecoderFfmpegFrameMetadataResult.Unsupported,
            PlaybackDecoderFfmpegNativeFrameResult.ContextUnavailable =>
                PlaybackDecoderFfmpegFrameMetadataResult.ContextUnavailable,
            PlaybackDecoderFfmpegNativeFrameResult.ContextNotOpened =>
                PlaybackDecoderFfmpegFrameMetadataResult.ContextNotOpened,
            PlaybackDecoderFfmpegNativeFrameResult.GateRejected =>
                PlaybackDecoderFfmpegFrameMetadataResult.GateRejected,
            PlaybackDecoderFfmpegNativeFrameResult.ConsumerRejected =>
                PlaybackDecoderFfmpegFrameMetadataResult.ConsumerRejected,
            _ => PlaybackDecoderFfmpegFrameMetadataResult.NativeFailure,
        };
    }

    private sealed class MetadataConsumerAdapter : IPlaybackDecoderFfmpegNativeFrameConsumer
    {
        private readonly IPlaybackDecoderFfmpegFrameMetadataNativeApi _nativeApi;
        private readonly PlaybackDecoderFfmpegCodecId _codecId;
        private readonly IPlaybackDecoderFfmpegFrameMetadataConsumer _consumer;

        public MetadataConsumerAdapter(
            IPlaybackDecoderFfmpegFrameMetadataNativeApi nativeApi,
            PlaybackDecoderFfmpegCodecId codecId,
            IPlaybackDecoderFfmpegFrameMetadataConsumer consumer)
        {
            _nativeApi = nativeApi;
            _codecId = codecId;
            _consumer = consumer;
        }

        public bool ValidationFailed { get; private set; }

        public bool Consume(nint frame)
        {
            var raw = _nativeApi.Read(frame);
            if (!TryCreate(_codecId, raw, out var metadata))
            {
                ValidationFailed = true;
                return false;
            }

            return _consumer.Consume(metadata);
        }
    }

    private static bool TryCreate(
        PlaybackDecoderFfmpegCodecId codecId,
        PlaybackDecoderFfmpegRawFrameMetadata raw,
        out PlaybackDecoderFfmpegFrameMetadata metadata)
    {
        metadata = default;
        if (raw.Duration < 0 ||
            raw.TimeBaseNumerator < 0 ||
            raw.TimeBaseDenominator <= 0 ||
            !TryConvertTime(raw.PresentationTimestamp, raw, false, out var timestamp) ||
            !TryConvertTime(raw.BestEffortTimestamp, raw, false, out var bestEffortTimestamp) ||
            !TryConvertTime(raw.Duration, raw, true, out var duration))
        {
            return false;
        }

        if (codecId == PlaybackDecoderFfmpegCodecId.H264)
        {
            if (raw.Width is < 1 or > PlaybackDecodedVideoFrame.MaximumDimension ||
                raw.Height is < 1 or > PlaybackDecodedVideoFrame.MaximumDimension ||
                (long)raw.Width * raw.Height >
                    (long)PlaybackDecodedVideoFrame.MaximumDimension *
                    PlaybackDecodedVideoFrame.MaximumDimension ||
                !TryMapVideoFormat(raw.Format, out var format))
            {
                return false;
            }

            metadata = new(
                PlaybackDecoderFfmpegFrameMediaKind.Video,
                format,
                raw.Width,
                raw.Height,
                0,
                0,
                0,
                timestamp,
                bestEffortTimestamp,
                duration);
            return true;
        }

        if (codecId == PlaybackDecoderFfmpegCodecId.Aac)
        {
            if (raw.SampleRate is < PlaybackDecodedAudioFrame.MinimumSampleRate or
                    > PlaybackDecodedAudioFrame.MaximumSampleRate ||
                raw.ChannelCount is < PlaybackDecodedAudioFrame.MinimumChannelCount or
                    > PlaybackDecodedAudioFrame.MaximumChannelCount ||
                raw.SampleCount is < 1 or > PlaybackDecodedAudioFrame.MaximumSampleCount ||
                !TryMapAudioFormat(raw.Format, out var format))
            {
                return false;
            }

            metadata = new(
                PlaybackDecoderFfmpegFrameMediaKind.Audio,
                format,
                0,
                0,
                raw.SampleRate,
                raw.ChannelCount,
                raw.SampleCount,
                timestamp,
                bestEffortTimestamp,
                duration);
            return true;
        }

        return false;
    }

    private static bool TryMapVideoFormat(
        int nativeFormat,
        out PlaybackDecoderFfmpegFrameFormat format)
    {
        format = nativeFormat switch
        {
            0 => PlaybackDecoderFfmpegFrameFormat.VideoYuv420P,
            23 => PlaybackDecoderFfmpegFrameFormat.VideoNv12,
            28 => PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
            _ => default,
        };
        return nativeFormat is 0 or 23 or 28;
    }

    private static bool TryMapAudioFormat(
        int nativeFormat,
        out PlaybackDecoderFfmpegFrameFormat format)
    {
        format = nativeFormat switch
        {
            1 => PlaybackDecoderFfmpegFrameFormat.AudioSigned16,
            3 => PlaybackDecoderFfmpegFrameFormat.AudioFloat32,
            6 => PlaybackDecoderFfmpegFrameFormat.AudioSigned16Planar,
            8 => PlaybackDecoderFfmpegFrameFormat.AudioFloat32Planar,
            _ => default,
        };
        return nativeFormat is 1 or 3 or 6 or 8;
    }

    private static bool TryConvertTime(
        long value,
        PlaybackDecoderFfmpegRawFrameMetadata raw,
        bool isDuration,
        out PlaybackDecoderFfmpegFrameTime converted)
    {
        converted = PlaybackDecoderFfmpegFrameTime.Unknown;
        if ((!isDuration && value == NoTimestamp) ||
            (isDuration && value == 0) ||
            raw.TimeBaseNumerator == 0)
        {
            return true;
        }

        try
        {
            var ticks = decimal.ToInt64(decimal.Truncate(
                (decimal)value * raw.TimeBaseNumerator * TimeSpan.TicksPerSecond /
                raw.TimeBaseDenominator));
            var time = TimeSpan.FromTicks(ticks);
            if (isDuration && time > PlaybackDecodedAudioFrame.MaximumDuration)
            {
                return false;
            }

            converted = PlaybackDecoderFfmpegFrameTime.Known(time);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}

internal sealed class WindowsFfmpeg81FrameMetadataNativeApi :
    IPlaybackDecoderFfmpegFrameMetadataNativeApi
{
    // Offsets are pinned to FFmpeg 8.1 libavutil 60 frame.h on Windows x64.
    private const int WidthOffset = 104;
    private const int HeightOffset = 108;
    private const int SampleCountOffset = 112;
    private const int FormatOffset = 116;
    private const int PresentationTimestampOffset = 136;
    private const int TimeBaseNumeratorOffset = 152;
    private const int TimeBaseDenominatorOffset = 156;
    private const int SampleRateOffset = 180;
    private const int BestEffortTimestampOffset = 304;
    private const int ChannelCountOffset = 388;
    private const int DurationOffset = 408;

    public PlaybackDecoderFfmpegRawFrameMetadata Read(nint frame)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || frame == 0)
        {
            throw new NotSupportedException("The frame metadata boundary is unavailable.");
        }

        return new(
            Marshal.ReadInt32(frame, FormatOffset),
            Marshal.ReadInt32(frame, WidthOffset),
            Marshal.ReadInt32(frame, HeightOffset),
            Marshal.ReadInt32(frame, SampleRateOffset),
            Marshal.ReadInt32(frame, ChannelCountOffset),
            Marshal.ReadInt32(frame, SampleCountOffset),
            Marshal.ReadInt64(frame, PresentationTimestampOffset),
            Marshal.ReadInt64(frame, BestEffortTimestampOffset),
            Marshal.ReadInt64(frame, DurationOffset),
            Marshal.ReadInt32(frame, TimeBaseNumeratorOffset),
            Marshal.ReadInt32(frame, TimeBaseDenominatorOffset));
    }
}
