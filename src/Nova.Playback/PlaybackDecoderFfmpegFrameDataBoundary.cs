using System.Runtime.InteropServices;

namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderFfmpegFrameDataResult
{
    Processed,
    NeedMoreInput,
    EndOfStream,
    Unsupported,
    ContextUnavailable,
    ContextNotOpened,
    GateRejected,
    InvalidMetadata,
    InvalidData,
    ConsumerRejected,
    NativeFailure,
}

public enum PlaybackDecoderFfmpegFrameDataKind
{
    VideoPlane,
    AudioSamples,
}

public readonly ref struct PlaybackDecoderFfmpegFrameDataView
{
    private readonly nint _data;

    internal PlaybackDecoderFfmpegFrameDataView(
        nint data,
        PlaybackDecoderFfmpegFrameDataKind kind,
        int index,
        int length,
        int stride,
        int rows)
    {
        _data = data;
        Kind = kind;
        Index = index;
        Length = length;
        Stride = stride;
        Rows = rows;
    }

    public PlaybackDecoderFfmpegFrameDataKind Kind { get; }

    public int Index { get; }

    public int Length { get; }

    public int Stride { get; }

    public int Rows { get; }

    public byte ReadByte(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, Length);

        return Marshal.ReadByte(_data, offset);
    }
}

public interface IPlaybackDecoderFfmpegFrameDataConsumer
{
    bool Consume(
        PlaybackDecoderFfmpegFrameMetadata metadata,
        PlaybackDecoderFfmpegFrameDataView data);
}

internal enum PlaybackDecoderFfmpegFrameDataVisitResult
{
    Consumed,
    InvalidData,
    ConsumerRejected,
}

internal interface IPlaybackDecoderFfmpegFrameMemoryReader
{
    nint ReadVideoDataPointer(nint frame, int planeIndex);

    nint ReadAudioDataPointer(nint frame, int planeIndex);

    int ReadLineSize(nint frame, int planeIndex);
}

internal interface IPlaybackDecoderFfmpegFrameDataNativeApi
{
    PlaybackDecoderFfmpegFrameDataVisitResult ValidateAndConsume(
        nint frame,
        PlaybackDecoderFfmpegFrameMetadata metadata,
        IPlaybackDecoderFfmpegFrameDataConsumer consumer);
}

public sealed class PlaybackDecoderFfmpegFrameDataBoundary
{
    private readonly PlaybackDecoderFfmpegNativeFrameBoundary _frameBoundary;
    private readonly PlaybackDecoderFfmpegFrameMetadataBoundary _metadataBoundary;
    private readonly IPlaybackDecoderFfmpegFrameDataNativeApi _nativeApi;
    private readonly bool _isSupportedPlatform;

    public PlaybackDecoderFfmpegFrameDataBoundary()
        : this(
            new PlaybackDecoderFfmpegNativeFrameBoundary(),
            new PlaybackDecoderFfmpegFrameMetadataBoundary(),
            new WindowsFfmpeg81FrameDataNativeApi(),
            OperatingSystem.IsWindows() && Environment.Is64BitProcess)
    {
    }

    internal PlaybackDecoderFfmpegFrameDataBoundary(
        PlaybackDecoderFfmpegNativeFrameBoundary frameBoundary,
        PlaybackDecoderFfmpegFrameMetadataBoundary metadataBoundary,
        IPlaybackDecoderFfmpegFrameDataNativeApi nativeApi,
        bool isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(frameBoundary);
        ArgumentNullException.ThrowIfNull(metadataBoundary);
        ArgumentNullException.ThrowIfNull(nativeApi);
        _frameBoundary = frameBoundary;
        _metadataBoundary = metadataBoundary;
        _nativeApi = nativeApi;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackDecoderFfmpegFrameDataResult ReceiveAndConsume(
        PlaybackDecoderFfmpegContextLease contextLease,
        IPlaybackDecoderFfmpegFrameDataConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(contextLease);
        ArgumentNullException.ThrowIfNull(consumer);
        if (!_isSupportedPlatform)
        {
            return PlaybackDecoderFfmpegFrameDataResult.Unsupported;
        }

        var adapter = new FrameConsumerAdapter(
            _metadataBoundary,
            _nativeApi,
            contextLease.CodecId,
            consumer);
        var frameResult = _frameBoundary.ReceiveAndProcess(contextLease, adapter);
        if (adapter.Result == PlaybackDecoderFfmpegFrameDataVisitResult.InvalidData)
        {
            return adapter.MetadataInvalid
                ? PlaybackDecoderFfmpegFrameDataResult.InvalidMetadata
                : PlaybackDecoderFfmpegFrameDataResult.InvalidData;
        }

        return frameResult switch
        {
            PlaybackDecoderFfmpegNativeFrameResult.Processed =>
                PlaybackDecoderFfmpegFrameDataResult.Processed,
            PlaybackDecoderFfmpegNativeFrameResult.NeedMoreInput =>
                PlaybackDecoderFfmpegFrameDataResult.NeedMoreInput,
            PlaybackDecoderFfmpegNativeFrameResult.EndOfStream =>
                PlaybackDecoderFfmpegFrameDataResult.EndOfStream,
            PlaybackDecoderFfmpegNativeFrameResult.Unsupported =>
                PlaybackDecoderFfmpegFrameDataResult.Unsupported,
            PlaybackDecoderFfmpegNativeFrameResult.ContextUnavailable =>
                PlaybackDecoderFfmpegFrameDataResult.ContextUnavailable,
            PlaybackDecoderFfmpegNativeFrameResult.ContextNotOpened =>
                PlaybackDecoderFfmpegFrameDataResult.ContextNotOpened,
            PlaybackDecoderFfmpegNativeFrameResult.GateRejected =>
                PlaybackDecoderFfmpegFrameDataResult.GateRejected,
            PlaybackDecoderFfmpegNativeFrameResult.ConsumerRejected =>
                PlaybackDecoderFfmpegFrameDataResult.ConsumerRejected,
            _ => PlaybackDecoderFfmpegFrameDataResult.NativeFailure,
        };
    }

    private sealed class FrameConsumerAdapter : IPlaybackDecoderFfmpegNativeFrameConsumer
    {
        private readonly PlaybackDecoderFfmpegFrameMetadataBoundary _metadataBoundary;
        private readonly IPlaybackDecoderFfmpegFrameDataNativeApi _nativeApi;
        private readonly PlaybackDecoderFfmpegCodecId _codecId;
        private readonly IPlaybackDecoderFfmpegFrameDataConsumer _consumer;

        public FrameConsumerAdapter(
            PlaybackDecoderFfmpegFrameMetadataBoundary metadataBoundary,
            IPlaybackDecoderFfmpegFrameDataNativeApi nativeApi,
            PlaybackDecoderFfmpegCodecId codecId,
            IPlaybackDecoderFfmpegFrameDataConsumer consumer)
        {
            _metadataBoundary = metadataBoundary;
            _nativeApi = nativeApi;
            _codecId = codecId;
            _consumer = consumer;
        }

        public bool MetadataInvalid { get; private set; }

        public PlaybackDecoderFfmpegFrameDataVisitResult Result { get; private set; }

        public bool Consume(nint frame)
        {
            if (!_metadataBoundary.TryReadCallbackMetadata(frame, _codecId, out var metadata))
            {
                MetadataInvalid = true;
                Result = PlaybackDecoderFfmpegFrameDataVisitResult.InvalidData;
                return false;
            }

            Result = _nativeApi.ValidateAndConsume(frame, metadata, _consumer);
            return Result == PlaybackDecoderFfmpegFrameDataVisitResult.Consumed;
        }
    }
}

internal sealed class WindowsFfmpeg81FrameDataNativeApi :
    IPlaybackDecoderFfmpegFrameDataNativeApi
{
    internal const int MaximumReferencedBytes = 256 * 1024 * 1024;
    private readonly IPlaybackDecoderFfmpegFrameMemoryReader _memory;
    private readonly bool _isSupportedPlatform;

    public WindowsFfmpeg81FrameDataNativeApi()
        : this(
            new WindowsFfmpeg81FrameMemoryReader(),
            OperatingSystem.IsWindows() && Environment.Is64BitProcess)
    {
    }

    internal WindowsFfmpeg81FrameDataNativeApi(
        IPlaybackDecoderFfmpegFrameMemoryReader memory,
        bool isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(memory);
        _memory = memory;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackDecoderFfmpegFrameDataVisitResult ValidateAndConsume(
        nint frame,
        PlaybackDecoderFfmpegFrameMetadata metadata,
        IPlaybackDecoderFfmpegFrameDataConsumer consumer)
    {
        if (!_isSupportedPlatform || frame == 0)
        {
            throw new NotSupportedException("The frame data boundary is unavailable.");
        }

        var descriptors = metadata.MediaKind switch
        {
            PlaybackDecoderFfmpegFrameMediaKind.Video => BuildVideo(frame, metadata),
            PlaybackDecoderFfmpegFrameMediaKind.Audio => BuildAudio(frame, metadata),
            _ => null,
        };
        if (descriptors is null)
        {
            return PlaybackDecoderFfmpegFrameDataVisitResult.InvalidData;
        }

        foreach (var descriptor in descriptors)
        {
            var view = new PlaybackDecoderFfmpegFrameDataView(
                descriptor.Pointer,
                descriptor.Kind,
                descriptor.Index,
                descriptor.Length,
                descriptor.Stride,
                descriptor.Rows);
            if (!consumer.Consume(metadata, view))
            {
                return PlaybackDecoderFfmpegFrameDataVisitResult.ConsumerRejected;
            }
        }

        return PlaybackDecoderFfmpegFrameDataVisitResult.Consumed;
    }

    private Descriptor[]? BuildVideo(
        nint frame,
        PlaybackDecoderFfmpegFrameMetadata metadata)
    {
        (int Width, int Height, int Bytes)[] planes;
        try
        {
            planes = metadata.Format switch
            {
                PlaybackDecoderFfmpegFrameFormat.VideoYuv420P =>
                [
                    (metadata.Width, metadata.Height, 1),
                    (checked((metadata.Width + 1) / 2), (metadata.Height + 1) / 2, 1),
                    (checked((metadata.Width + 1) / 2), (metadata.Height + 1) / 2, 1),
                ],
                PlaybackDecoderFfmpegFrameFormat.VideoNv12 =>
                [
                    (metadata.Width, metadata.Height, 1),
                    (checked(((metadata.Width + 1) / 2) * 2), (metadata.Height + 1) / 2, 1),
                ],
                PlaybackDecoderFfmpegFrameFormat.VideoBgra32 =>
                    [(metadata.Width, metadata.Height, 4)],
                _ => [],
            };
        }
        catch (OverflowException)
        {
            return null;
        }
        if (planes.Length == 0)
        {
            return null;
        }

        var descriptors = new Descriptor[planes.Length];
        long total = 0;
        for (var index = 0; index < planes.Length; index++)
        {
            var pointer = _memory.ReadVideoDataPointer(frame, index);
            var stride = _memory.ReadLineSize(frame, index);
            var minimumStride = checked(planes[index].Width * planes[index].Bytes);
            if (pointer == 0 || stride < minimumStride ||
                stride > PlaybackDecodedVideoFrame.MaximumStride ||
                !TryLength(stride, planes[index].Height, ref total, out var length))
            {
                return null;
            }

            descriptors[index] = new(
                pointer,
                PlaybackDecoderFfmpegFrameDataKind.VideoPlane,
                index,
                length,
                stride,
                planes[index].Height);
        }

        return descriptors;
    }

    private Descriptor[]? BuildAudio(
        nint frame,
        PlaybackDecoderFfmpegFrameMetadata metadata)
    {
        var (bytesPerSample, planar) = metadata.Format switch
        {
            PlaybackDecoderFfmpegFrameFormat.AudioSigned16 => (2, false),
            PlaybackDecoderFfmpegFrameFormat.AudioFloat32 => (4, false),
            PlaybackDecoderFfmpegFrameFormat.AudioSigned16Planar => (2, true),
            PlaybackDecoderFfmpegFrameFormat.AudioFloat32Planar => (4, true),
            _ => (0, false),
        };
        if (bytesPerSample == 0)
        {
            return null;
        }

        var planeCount = planar ? metadata.ChannelCount : 1;
        var samplesPerPlane = planar
            ? metadata.SampleCount
            : checked(metadata.SampleCount * metadata.ChannelCount);
        var logicalLength = checked(samplesPerPlane * bytesPerSample);
        var lineSize = _memory.ReadLineSize(frame, 0);
        if (lineSize < logicalLength || lineSize > MaximumReferencedBytes)
        {
            return null;
        }

        var descriptors = new Descriptor[planeCount];
        long total = 0;
        for (var index = 0; index < planeCount; index++)
        {
            var pointer = _memory.ReadAudioDataPointer(frame, index);
            if (pointer == 0 || !TryLength(logicalLength, 1, ref total, out var length))
            {
                return null;
            }

            descriptors[index] = new(
                pointer,
                PlaybackDecoderFfmpegFrameDataKind.AudioSamples,
                index,
                length,
                lineSize,
                1);
        }

        return descriptors;
    }

    private static bool TryLength(
        int stride,
        int rows,
        ref long total,
        out int length)
    {
        length = 0;
        try
        {
            var candidate = checked((long)stride * rows);
            total = checked(total + candidate);
            if (candidate is < 1 or > int.MaxValue || total > MaximumReferencedBytes)
            {
                return false;
            }

            length = (int)candidate;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private sealed record Descriptor(
        nint Pointer,
        PlaybackDecoderFfmpegFrameDataKind Kind,
        int Index,
        int Length,
        int Stride,
        int Rows);
}

internal sealed class WindowsFfmpeg81FrameMemoryReader :
    IPlaybackDecoderFfmpegFrameMemoryReader
{
    private const int DataOffset = 0;
    private const int LineSizeOffset = 64;
    private const int ExtendedDataOffset = 96;

    public nint ReadVideoDataPointer(nint frame, int planeIndex) =>
        Marshal.ReadIntPtr(frame, DataOffset + (planeIndex * IntPtr.Size));

    public nint ReadAudioDataPointer(nint frame, int planeIndex)
    {
        var extendedData = Marshal.ReadIntPtr(frame, ExtendedDataOffset);
        return extendedData == 0
            ? 0
            : Marshal.ReadIntPtr(extendedData, planeIndex * IntPtr.Size);
    }

    public int ReadLineSize(nint frame, int planeIndex) =>
        Marshal.ReadInt32(frame, LineSizeOffset + (planeIndex * sizeof(int)));
}
