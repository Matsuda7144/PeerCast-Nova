namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderFfmpegManagedFormatConversionResult
{
    Converted,
    ContextUnavailable,
    DisposedFrame,
    UnsupportedFormat,
    InvalidFrame,
    AllocationFailed,
    ConversionFailed,
    ConsumerRejected,
}

internal interface IPlaybackDecoderManagedFormatConversionWorker
{
    void Convert(
        PlaybackDecoderFfmpegManagedFrame frame,
        Span<byte> destination);
}

public sealed class PlaybackDecoderFfmpegManagedFormatConversionBoundary
{
    private readonly IPlaybackDecoderManagedBufferAllocator _allocator;
    private readonly IPlaybackDecoderManagedFormatConversionWorker _worker;

    public PlaybackDecoderFfmpegManagedFormatConversionBoundary()
        : this(
            new PlaybackDecoderManagedBufferAllocator(),
            new PlaybackDecoderManagedFormatConversionWorker())
    {
    }

    internal PlaybackDecoderFfmpegManagedFormatConversionBoundary(
        IPlaybackDecoderManagedBufferAllocator allocator,
        IPlaybackDecoderManagedFormatConversionWorker worker)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(worker);
        _allocator = allocator;
        _worker = worker;
    }

    public PlaybackDecoderFfmpegManagedFormatConversionResult Convert(
        PlaybackDecoderFfmpegContextLease contextLease,
        PlaybackDecoderFfmpegManagedFrame frame,
        long sequence,
        PlaybackDecodedFrameDiscontinuityKind discontinuity,
        IPlaybackDecoderFfmpegDecodedOutputConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(contextLease);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(consumer);
        if (contextLease.IsDisposed)
        {
            return PlaybackDecoderFfmpegManagedFormatConversionResult.ContextUnavailable;
        }

        if (frame.IsDisposed)
        {
            return PlaybackDecoderFfmpegManagedFormatConversionResult.DisposedFrame;
        }

        var validation = Validate(frame, out var output);
        if (validation != PlaybackDecoderFfmpegManagedFormatConversionResult.Converted)
        {
            return validation;
        }

        byte[] payload;
        try
        {
            payload = _allocator.Allocate(output.PayloadLength);
            if (payload.Length != output.PayloadLength)
            {
                Array.Clear(payload);
                return PlaybackDecoderFfmpegManagedFormatConversionResult.AllocationFailed;
            }
        }
        catch (OutOfMemoryException)
        {
            return PlaybackDecoderFfmpegManagedFormatConversionResult.AllocationFailed;
        }

        try
        {
            _worker.Convert(frame, payload);
        }
        catch (Exception exception) when (exception is
            ArgumentException or IndexOutOfRangeException or InvalidOperationException or
            OverflowException)
        {
            Array.Clear(payload);
            return PlaybackDecoderFfmpegManagedFormatConversionResult.ConversionFailed;
        }

        var metadata = frame.Metadata;
        var timestamp = metadata.PresentationTimestamp.IsKnown
            ? metadata.PresentationTimestamp.Value
            : PlaybackDecoderFfmpegDecodedOutputAdapter.UnknownTimestamp;
        var duration = metadata.Duration.IsKnown
            ? metadata.Duration.Value
            : PlaybackDecoderFfmpegDecodedOutputAdapter.UnknownDuration;
        try
        {
            bool accepted;
            try
            {
                accepted = output.Kind switch
                {
                    OutputKind.Video => consumer.ConsumeVideo(
                        PlaybackDecodedVideoFrame.Create(
                            sequence,
                            timestamp,
                            duration,
                            discontinuity,
                            PlaybackDecodedVideoPixelFormat.Bgra32,
                            metadata.Width,
                            metadata.Height,
                            output.Stride,
                            payload)),
                    OutputKind.AudioSigned16 => consumer.ConsumeAudio(
                        PlaybackDecodedAudioFrame.Create(
                            sequence,
                            timestamp,
                            duration,
                            discontinuity,
                            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                            metadata.SampleRate,
                            metadata.ChannelCount,
                            metadata.SampleCount,
                            payload)),
                    OutputKind.AudioFloat32 => consumer.ConsumeAudio(
                        PlaybackDecodedAudioFrame.Create(
                            sequence,
                            timestamp,
                            duration,
                            discontinuity,
                            PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved,
                            metadata.SampleRate,
                            metadata.ChannelCount,
                            metadata.SampleCount,
                            payload)),
                    _ => false,
                };
            }
            catch (Exception exception) when (exception is
                ArgumentException or OverflowException)
            {
                return PlaybackDecoderFfmpegManagedFormatConversionResult.InvalidFrame;
            }

            return accepted
                ? PlaybackDecoderFfmpegManagedFormatConversionResult.Converted
                : PlaybackDecoderFfmpegManagedFormatConversionResult.ConsumerRejected;
        }
        finally
        {
            Array.Clear(payload);
        }
    }

    private static PlaybackDecoderFfmpegManagedFormatConversionResult Validate(
        PlaybackDecoderFfmpegManagedFrame frame,
        out OutputLayout output)
    {
        output = default;
        var metadata = frame.Metadata;
        if (!Enum.IsDefined(metadata.MediaKind) || !Enum.IsDefined(metadata.Format))
        {
            return PlaybackDecoderFfmpegManagedFormatConversionResult.UnsupportedFormat;
        }

        if (!IsTargetFormat(metadata.Format))
        {
            return PlaybackDecoderFfmpegManagedFormatConversionResult.UnsupportedFormat;
        }

        if (!IsBoundedMetadata(metadata))
        {
            return PlaybackDecoderFfmpegManagedFormatConversionResult.InvalidFrame;
        }

        PlaneLayout[] expected;
        try
        {
            expected = CreateExpected(metadata, out output);
        }
        catch (OverflowException)
        {
            return PlaybackDecoderFfmpegManagedFormatConversionResult.InvalidFrame;
        }

        if (expected.Length == 0)
        {
            return PlaybackDecoderFfmpegManagedFormatConversionResult.UnsupportedFormat;
        }

        if (frame.Planes.Count != expected.Length)
        {
            return PlaybackDecoderFfmpegManagedFormatConversionResult.InvalidFrame;
        }

        try
        {
            long inputTotal = 0;
            for (var index = 0; index < expected.Length; index++)
            {
                var plane = frame.Planes[index];
                var length = checked(expected[index].LogicalRowBytes * expected[index].Rows);
                inputTotal = checked(inputTotal + length);
                if (plane.Index != index ||
                    plane.Rows != expected[index].Rows ||
                    plane.LogicalRowBytes != expected[index].LogicalRowBytes ||
                    plane.Stride < plane.LogicalRowBytes ||
                    plane.Buffer.Length != length ||
                    inputTotal > PlaybackDecoderFfmpegManagedFrameCopyBoundary.MaximumManagedBytes)
                {
                    return PlaybackDecoderFfmpegManagedFormatConversionResult.InvalidFrame;
                }
            }

            if (output.PayloadLength < 1 ||
                output.PayloadLength > PlaybackDecoderFfmpegManagedFrameCopyBoundary.MaximumManagedBytes)
            {
                return PlaybackDecoderFfmpegManagedFormatConversionResult.InvalidFrame;
            }

            return PlaybackDecoderFfmpegManagedFormatConversionResult.Converted;
        }
        catch (OverflowException)
        {
            return PlaybackDecoderFfmpegManagedFormatConversionResult.InvalidFrame;
        }
    }

    private static bool IsTargetFormat(PlaybackDecoderFfmpegFrameFormat format) => format is
        PlaybackDecoderFfmpegFrameFormat.VideoYuv420P or
        PlaybackDecoderFfmpegFrameFormat.VideoNv12 or
        PlaybackDecoderFfmpegFrameFormat.AudioSigned16Planar or
        PlaybackDecoderFfmpegFrameFormat.AudioFloat32Planar;

    private static bool IsBoundedMetadata(PlaybackDecoderFfmpegFrameMetadata metadata)
    {
        if (metadata.MediaKind == PlaybackDecoderFfmpegFrameMediaKind.Video)
        {
            return metadata.Format is
                    PlaybackDecoderFfmpegFrameFormat.VideoYuv420P or
                    PlaybackDecoderFfmpegFrameFormat.VideoNv12 &&
                metadata.Width is >= 1 and <= PlaybackDecodedVideoFrame.MaximumDimension &&
                metadata.Height is >= 1 and <= PlaybackDecodedVideoFrame.MaximumDimension;
        }

        return metadata.MediaKind == PlaybackDecoderFfmpegFrameMediaKind.Audio &&
            metadata.Format is
                PlaybackDecoderFfmpegFrameFormat.AudioSigned16Planar or
                PlaybackDecoderFfmpegFrameFormat.AudioFloat32Planar &&
            metadata.SampleRate is >= PlaybackDecodedAudioFrame.MinimumSampleRate and
                <= PlaybackDecodedAudioFrame.MaximumSampleRate &&
            metadata.ChannelCount is >= PlaybackDecodedAudioFrame.MinimumChannelCount and
                <= PlaybackDecodedAudioFrame.MaximumChannelCount &&
            metadata.SampleCount is >= 1 and <= PlaybackDecodedAudioFrame.MaximumSampleCount;
    }

    private static PlaneLayout[] CreateExpected(
        PlaybackDecoderFfmpegFrameMetadata metadata,
        out OutputLayout output)
    {
        output = default;
        if (metadata.MediaKind == PlaybackDecoderFfmpegFrameMediaKind.Video &&
            metadata.Width is >= 1 and <= PlaybackDecodedVideoFrame.MaximumDimension &&
            metadata.Height is >= 1 and <= PlaybackDecodedVideoFrame.MaximumDimension)
        {
            var stride = checked(metadata.Width * 4);
            output = new(OutputKind.Video, stride, checked(stride * metadata.Height));
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
            PlaybackDecoderFfmpegFrameFormat.AudioSigned16Planar =>
                CreateAudio(metadata, 2, OutputKind.AudioSigned16, out output),
            PlaybackDecoderFfmpegFrameFormat.AudioFloat32Planar =>
                CreateAudio(metadata, 4, OutputKind.AudioFloat32, out output),
            _ => [],
        };
    }

    private static PlaneLayout[] CreateAudio(
        PlaybackDecoderFfmpegFrameMetadata metadata,
        int bytesPerSample,
        OutputKind kind,
        out OutputLayout output)
    {
        var planeBytes = checked(metadata.SampleCount * bytesPerSample);
        output = new(
            kind,
            0,
            checked(planeBytes * metadata.ChannelCount));
        return Enumerable.Range(0, metadata.ChannelCount)
            .Select(_ => new PlaneLayout(planeBytes, 1))
            .ToArray();
    }

    private enum OutputKind
    {
        Video,
        AudioSigned16,
        AudioFloat32,
    }

    private readonly record struct PlaneLayout(int LogicalRowBytes, int Rows);

    private readonly record struct OutputLayout(OutputKind Kind, int Stride, int PayloadLength);
}

internal sealed class PlaybackDecoderManagedFormatConversionWorker :
    IPlaybackDecoderManagedFormatConversionWorker
{
    public void Convert(
        PlaybackDecoderFfmpegManagedFrame frame,
        Span<byte> destination)
    {
        switch (frame.Metadata.Format)
        {
            case PlaybackDecoderFfmpegFrameFormat.VideoYuv420P:
                ConvertYuv420P(frame, destination);
                break;
            case PlaybackDecoderFfmpegFrameFormat.VideoNv12:
                ConvertNv12(frame, destination);
                break;
            case PlaybackDecoderFfmpegFrameFormat.AudioSigned16Planar:
                Interleave(frame, 2, destination);
                break;
            case PlaybackDecoderFfmpegFrameFormat.AudioFloat32Planar:
                Interleave(frame, 4, destination);
                break;
            default:
                throw new NotSupportedException("The managed conversion format is unsupported.");
        }
    }

    private static void ConvertYuv420P(
        PlaybackDecoderFfmpegManagedFrame frame,
        Span<byte> destination)
    {
        var metadata = frame.Metadata;
        var yPlane = frame.Planes[0].Buffer.Span;
        var uPlane = frame.Planes[1].Buffer.Span;
        var vPlane = frame.Planes[2].Buffer.Span;
        var chromaWidth = (metadata.Width + 1) / 2;
        for (var y = 0; y < metadata.Height; y++)
        {
            for (var x = 0; x < metadata.Width; x++)
            {
                var chroma = ((y / 2) * chromaWidth) + (x / 2);
                WriteBgra(
                    yPlane[(y * metadata.Width) + x],
                    uPlane[chroma],
                    vPlane[chroma],
                    destination,
                    ((y * metadata.Width) + x) * 4);
            }
        }
    }

    private static void ConvertNv12(
        PlaybackDecoderFfmpegManagedFrame frame,
        Span<byte> destination)
    {
        var metadata = frame.Metadata;
        var yPlane = frame.Planes[0].Buffer.Span;
        var uvPlane = frame.Planes[1].Buffer.Span;
        var chromaRowBytes = ((metadata.Width + 1) / 2) * 2;
        for (var y = 0; y < metadata.Height; y++)
        {
            for (var x = 0; x < metadata.Width; x++)
            {
                var chroma = ((y / 2) * chromaRowBytes) + ((x / 2) * 2);
                WriteBgra(
                    yPlane[(y * metadata.Width) + x],
                    uvPlane[chroma],
                    uvPlane[chroma + 1],
                    destination,
                    ((y * metadata.Width) + x) * 4);
            }
        }
    }

    private static void Interleave(
        PlaybackDecoderFfmpegManagedFrame frame,
        int bytesPerSample,
        Span<byte> destination)
    {
        var metadata = frame.Metadata;
        for (var sample = 0; sample < metadata.SampleCount; sample++)
        {
            for (var channel = 0; channel < metadata.ChannelCount; channel++)
            {
                var source = frame.Planes[channel].Buffer.Span;
                var sourceOffset = sample * bytesPerSample;
                var destinationOffset =
                    ((sample * metadata.ChannelCount) + channel) * bytesPerSample;
                source.Slice(sourceOffset, bytesPerSample)
                    .CopyTo(destination.Slice(destinationOffset, bytesPerSample));
            }
        }
    }

    private static void WriteBgra(
        byte y,
        byte u,
        byte v,
        Span<byte> destination,
        int offset)
    {
        var c = y - 16;
        var d = u - 128;
        var e = v - 128;
        destination[offset] = Clamp((298 * c) + (516 * d) + 128);
        destination[offset + 1] = Clamp((298 * c) - (100 * d) - (208 * e) + 128);
        destination[offset + 2] = Clamp((298 * c) + (409 * e) + 128);
        destination[offset + 3] = 255;
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value >> 8, 0, 255);
}
