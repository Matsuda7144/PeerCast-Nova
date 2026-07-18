namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderFfmpegDecodedOutputAdapterResult
{
    Converted,
    ConversionRequired,
    ContextUnavailable,
    DisposedFrame,
    InvalidFrame,
    AllocationFailed,
    ConsumerRejected,
}

public interface IPlaybackDecoderFfmpegDecodedOutputConsumer
{
    bool ConsumeAudio(PlaybackDecodedAudioFrame frame);

    bool ConsumeVideo(PlaybackDecodedVideoFrame frame);
}

internal interface IPlaybackDecoderFfmpegDecodedOutputAsyncConsumer
{
    ValueTask<bool> ConsumeAudioAsync(
        PlaybackDecodedAudioFrame frame,
        CancellationToken cancellationToken);

    ValueTask<bool> ConsumeVideoAsync(
        PlaybackDecodedVideoFrame frame,
        CancellationToken cancellationToken);
}

public sealed class PlaybackDecoderFfmpegDecodedOutputAdapter
{
    internal static readonly TimeSpan UnknownTimestamp = TimeSpan.Zero;
    internal static readonly TimeSpan UnknownDuration = TimeSpan.FromTicks(1);
    private readonly IPlaybackDecoderManagedBufferAllocator _allocator;

    public PlaybackDecoderFfmpegDecodedOutputAdapter()
        : this(new PlaybackDecoderManagedBufferAllocator())
    {
    }

    internal PlaybackDecoderFfmpegDecodedOutputAdapter(
        IPlaybackDecoderManagedBufferAllocator allocator)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        _allocator = allocator;
    }

    public PlaybackDecoderFfmpegDecodedOutputAdapterResult Adapt(
        PlaybackDecoderFfmpegContextLease contextLease,
        PlaybackDecoderFfmpegManagedFrame frame,
        long sequence,
        PlaybackDecodedFrameDiscontinuityKind discontinuity,
        IPlaybackDecoderFfmpegDecodedOutputConsumer consumer)
        => AdaptAsync(
            contextLease,
            frame,
            sequence,
            discontinuity,
            new SynchronousConsumer(consumer),
            CancellationToken.None).AsTask().GetAwaiter().GetResult();

    internal async ValueTask<PlaybackDecoderFfmpegDecodedOutputAdapterResult> AdaptAsync(
        PlaybackDecoderFfmpegContextLease contextLease,
        PlaybackDecoderFfmpegManagedFrame frame,
        long sequence,
        PlaybackDecodedFrameDiscontinuityKind discontinuity,
        IPlaybackDecoderFfmpegDecodedOutputAsyncConsumer consumer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contextLease);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(consumer);
        if (contextLease.IsDisposed)
        {
            return PlaybackDecoderFfmpegDecodedOutputAdapterResult.ContextUnavailable;
        }

        if (frame.IsDisposed)
        {
            return PlaybackDecoderFfmpegDecodedOutputAdapterResult.DisposedFrame;
        }

        if (!TryValidate(frame, out var layout))
        {
            return PlaybackDecoderFfmpegDecodedOutputAdapterResult.InvalidFrame;
        }

        if (layout.OutputKind == OutputKind.ConversionRequired)
        {
            return PlaybackDecoderFfmpegDecodedOutputAdapterResult.ConversionRequired;
        }

        byte[] payload;
        try
        {
            payload = _allocator.Allocate(layout.PayloadLength);
            if (payload.Length != layout.PayloadLength)
            {
                Array.Clear(payload);
                return PlaybackDecoderFfmpegDecodedOutputAdapterResult.AllocationFailed;
            }

        }
        catch (OutOfMemoryException)
        {
            return PlaybackDecoderFfmpegDecodedOutputAdapterResult.AllocationFailed;
        }

        try
        {
            frame.Planes[0].Buffer.Span.CopyTo(payload);
        }
        catch
        {
            Array.Clear(payload);
            throw;
        }

        try
        {
            var metadata = frame.Metadata;
            var timestamp = metadata.PresentationTimestamp.IsKnown
                ? metadata.PresentationTimestamp.Value
                : UnknownTimestamp;
            var duration = metadata.Duration.IsKnown
                ? metadata.Duration.Value
                : UnknownDuration;
            PlaybackDecodedVideoFrame video = default;
            PlaybackDecodedAudioFrame audio = default;
            try
            {
                switch (layout.OutputKind)
                {
                    case OutputKind.Video:
                        video = PlaybackDecodedVideoFrame.Create(
                        sequence,
                        timestamp,
                        duration,
                        discontinuity,
                        PlaybackDecodedVideoPixelFormat.Bgra32,
                        metadata.Width,
                        metadata.Height,
                        layout.LogicalRowBytes,
                        payload);
                        break;
                    case OutputKind.AudioSigned16:
                        audio = PlaybackDecodedAudioFrame.Create(
                        sequence,
                        timestamp,
                        duration,
                        discontinuity,
                        PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                        metadata.SampleRate,
                        metadata.ChannelCount,
                        metadata.SampleCount,
                        payload);
                        break;
                    case OutputKind.AudioFloat32:
                        audio = PlaybackDecodedAudioFrame.Create(
                        sequence,
                        timestamp,
                        duration,
                        discontinuity,
                        PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved,
                        metadata.SampleRate,
                        metadata.ChannelCount,
                        metadata.SampleCount,
                        payload);
                        break;
                    default:
                        return PlaybackDecoderFfmpegDecodedOutputAdapterResult.InvalidFrame;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or OverflowException)
            {
                return PlaybackDecoderFfmpegDecodedOutputAdapterResult.InvalidFrame;
            }

            var accepted = layout.OutputKind == OutputKind.Video
                ? await consumer.ConsumeVideoAsync(video, cancellationToken).ConfigureAwait(false)
                : await consumer.ConsumeAudioAsync(audio, cancellationToken).ConfigureAwait(false);
            return accepted
                ? PlaybackDecoderFfmpegDecodedOutputAdapterResult.Converted
                : PlaybackDecoderFfmpegDecodedOutputAdapterResult.ConsumerRejected;
        }
        finally
        {
            Array.Clear(payload);
        }
    }

    private sealed class SynchronousConsumer(IPlaybackDecoderFfmpegDecodedOutputConsumer consumer) :
        IPlaybackDecoderFfmpegDecodedOutputAsyncConsumer
    {
        public ValueTask<bool> ConsumeAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(consumer.ConsumeAudio(frame));

        public ValueTask<bool> ConsumeVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(consumer.ConsumeVideo(frame));
    }

    private static bool TryValidate(
        PlaybackDecoderFfmpegManagedFrame frame,
        out ValidatedLayout layout)
    {
        layout = default;
        var metadata = frame.Metadata;
        if (!Enum.IsDefined(metadata.MediaKind) ||
            !Enum.IsDefined(metadata.Format))
        {
            return false;
        }

        try
        {
            var expected = CreateExpectedLayouts(metadata, out var outputKind);
            if (expected.Length == 0 || frame.Planes.Count != expected.Length)
            {
                return false;
            }

            long total = 0;
            for (var index = 0; index < expected.Length; index++)
            {
                var plane = frame.Planes[index];
                var length = checked(expected[index].LogicalRowBytes * expected[index].Rows);
                total = checked(total + length);
                if (plane.Index != index ||
                    plane.Rows != expected[index].Rows ||
                    plane.LogicalRowBytes != expected[index].LogicalRowBytes ||
                    plane.Stride < plane.LogicalRowBytes ||
                    plane.Buffer.Length != length ||
                    total > PlaybackDecoderFfmpegManagedFrameCopyBoundary.MaximumManagedBytes)
                {
                    return false;
                }
            }

            layout = new(
                outputKind,
                expected[0].LogicalRowBytes,
                checked(expected[0].LogicalRowBytes * expected[0].Rows));
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentOutOfRangeException or OverflowException)
        {
            return false;
        }
    }

    private static PlaneLayout[] CreateExpectedLayouts(
        PlaybackDecoderFfmpegFrameMetadata metadata,
        out OutputKind outputKind)
    {
        outputKind = OutputKind.ConversionRequired;
        if (metadata.MediaKind == PlaybackDecoderFfmpegFrameMediaKind.Video &&
            metadata.Width is >= 1 and <= PlaybackDecodedVideoFrame.MaximumDimension &&
            metadata.Height is >= 1 and <= PlaybackDecodedVideoFrame.MaximumDimension)
        {
            return metadata.Format switch
            {
                PlaybackDecoderFfmpegFrameFormat.VideoYuv420P =>
                [
                    new(metadata.Width, metadata.Height),
                    new(checked((metadata.Width + 1) / 2), checked((metadata.Height + 1) / 2)),
                    new(checked((metadata.Width + 1) / 2), checked((metadata.Height + 1) / 2)),
                ],
                PlaybackDecoderFfmpegFrameFormat.VideoNv12 =>
                [
                    new(metadata.Width, metadata.Height),
                    new(checked(((metadata.Width + 1) / 2) * 2), checked((metadata.Height + 1) / 2)),
                ],
                PlaybackDecoderFfmpegFrameFormat.VideoBgra32 =>
                    CreateBgraLayout(metadata, out outputKind),
                _ => [],
            };
        }

        if (metadata.MediaKind != PlaybackDecoderFfmpegFrameMediaKind.Audio ||
            metadata.SampleRate is < PlaybackDecodedAudioFrame.MinimumSampleRate or
                > PlaybackDecodedAudioFrame.MaximumSampleRate ||
            metadata.ChannelCount is < PlaybackDecodedAudioFrame.MinimumChannelCount or
                > PlaybackDecodedAudioFrame.MaximumChannelCount ||
            metadata.SampleCount is < 1 or > PlaybackDecodedAudioFrame.MaximumSampleCount)
        {
            return [];
        }

        return metadata.Format switch
        {
            PlaybackDecoderFfmpegFrameFormat.AudioSigned16 =>
                CreatePackedAudioLayout(metadata, 2, OutputKind.AudioSigned16, out outputKind),
            PlaybackDecoderFfmpegFrameFormat.AudioFloat32 =>
                CreatePackedAudioLayout(metadata, 4, OutputKind.AudioFloat32, out outputKind),
            PlaybackDecoderFfmpegFrameFormat.AudioSigned16Planar =>
                CreatePlanarAudioLayout(metadata, 2),
            PlaybackDecoderFfmpegFrameFormat.AudioFloat32Planar =>
                CreatePlanarAudioLayout(metadata, 4),
            _ => [],
        };
    }

    private static PlaneLayout[] CreateBgraLayout(
        PlaybackDecoderFfmpegFrameMetadata metadata,
        out OutputKind outputKind)
    {
        outputKind = OutputKind.Video;
        return [new(checked(metadata.Width * 4), metadata.Height)];
    }

    private static PlaneLayout[] CreatePackedAudioLayout(
        PlaybackDecoderFfmpegFrameMetadata metadata,
        int bytesPerSample,
        OutputKind kind,
        out OutputKind outputKind)
    {
        outputKind = kind;
        return
        [
            new(
                checked(metadata.SampleCount * metadata.ChannelCount * bytesPerSample),
                1),
        ];
    }

    private static PlaneLayout[] CreatePlanarAudioLayout(
        PlaybackDecoderFfmpegFrameMetadata metadata,
        int bytesPerSample) => Enumerable.Range(0, metadata.ChannelCount)
            .Select(_ => new PlaneLayout(checked(metadata.SampleCount * bytesPerSample), 1))
            .ToArray();

    private enum OutputKind
    {
        ConversionRequired,
        Video,
        AudioSigned16,
        AudioFloat32,
    }

    private readonly record struct PlaneLayout(int LogicalRowBytes, int Rows);

    private readonly record struct ValidatedLayout(
        OutputKind OutputKind,
        int LogicalRowBytes,
        int PayloadLength);
}
