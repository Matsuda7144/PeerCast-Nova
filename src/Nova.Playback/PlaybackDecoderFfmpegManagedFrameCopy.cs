namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderFfmpegManagedFrameCopyResult
{
    Copied,
    NeedMoreInput,
    EndOfStream,
    Unsupported,
    ContextUnavailable,
    ContextNotOpened,
    GateRejected,
    InvalidMetadata,
    InsufficientData,
    AllocationFailed,
    ConsumerRejected,
    NativeFailure,
}

public sealed class PlaybackDecoderFfmpegManagedPlane
{
    private readonly byte[] _buffer;

    internal PlaybackDecoderFfmpegManagedPlane(
        int index,
        int stride,
        int rows,
        int logicalRowBytes,
        byte[] buffer)
    {
        Index = index;
        Stride = stride;
        Rows = rows;
        LogicalRowBytes = logicalRowBytes;
        _buffer = buffer;
    }

    public int Index { get; }

    public int Stride { get; }

    public int Rows { get; }

    public int LogicalRowBytes { get; }

    public ReadOnlyMemory<byte> Buffer => _buffer;

    internal void Clear() => Array.Clear(_buffer);
}

public sealed class PlaybackDecoderFfmpegManagedFrame : IDisposable
{
    private readonly PlaybackDecoderFfmpegManagedPlane[] _planes;
    private int _disposed;

    internal PlaybackDecoderFfmpegManagedFrame(
        PlaybackDecoderFfmpegFrameMetadata metadata,
        PlaybackDecoderFfmpegManagedPlane[] planes)
    {
        Metadata = metadata;
        _planes = planes;
    }

    public PlaybackDecoderFfmpegFrameMetadata Metadata { get; }

    public IReadOnlyList<PlaybackDecoderFfmpegManagedPlane> Planes => _planes;

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var plane in _planes)
        {
            plane.Clear();
        }
    }
}

public interface IPlaybackDecoderFfmpegManagedFrameConsumer
{
    bool Consume(PlaybackDecoderFfmpegManagedFrame frame);
}

internal interface IPlaybackDecoderManagedBufferAllocator
{
    byte[] Allocate(int length);
}

public sealed class PlaybackDecoderFfmpegManagedFrameCopyBoundary
{
    internal const int MaximumManagedBytes =
        WindowsFfmpeg81FrameDataNativeApi.MaximumReferencedBytes;

    private readonly IPlaybackDecoderFfmpegFrameDataSource _source;
    private readonly IPlaybackDecoderManagedBufferAllocator _allocator;

    public PlaybackDecoderFfmpegManagedFrameCopyBoundary()
        : this(
            new PlaybackDecoderFfmpegFrameDataBoundary(),
            new PlaybackDecoderManagedBufferAllocator())
    {
    }

    internal PlaybackDecoderFfmpegManagedFrameCopyBoundary(
        IPlaybackDecoderFfmpegFrameDataSource source,
        IPlaybackDecoderManagedBufferAllocator allocator)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(allocator);
        _source = source;
        _allocator = allocator;
    }

    public PlaybackDecoderFfmpegManagedFrameCopyResult ReceiveAndCopy(
        PlaybackDecoderFfmpegContextLease contextLease,
        IPlaybackDecoderFfmpegManagedFrameConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(contextLease);
        ArgumentNullException.ThrowIfNull(consumer);
        var adapter = new CopyConsumer(consumer, _allocator);
        var result = _source.ReceiveAndConsume(contextLease, adapter);
        if (result == PlaybackDecoderFfmpegFrameDataResult.Processed &&
            !adapter.IsComplete)
        {
            return PlaybackDecoderFfmpegManagedFrameCopyResult.InsufficientData;
        }

        return adapter.Failure switch
        {
            CopyFailure.InvalidMetadata =>
                PlaybackDecoderFfmpegManagedFrameCopyResult.InvalidMetadata,
            CopyFailure.InsufficientData =>
                PlaybackDecoderFfmpegManagedFrameCopyResult.InsufficientData,
            CopyFailure.AllocationFailed =>
                PlaybackDecoderFfmpegManagedFrameCopyResult.AllocationFailed,
            CopyFailure.ConsumerRejected =>
                PlaybackDecoderFfmpegManagedFrameCopyResult.ConsumerRejected,
            _ => Map(result),
        };
    }

    private static PlaybackDecoderFfmpegManagedFrameCopyResult Map(
        PlaybackDecoderFfmpegFrameDataResult result) => result switch
        {
            PlaybackDecoderFfmpegFrameDataResult.Processed =>
                PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            PlaybackDecoderFfmpegFrameDataResult.NeedMoreInput =>
                PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput,
            PlaybackDecoderFfmpegFrameDataResult.EndOfStream =>
                PlaybackDecoderFfmpegManagedFrameCopyResult.EndOfStream,
            PlaybackDecoderFfmpegFrameDataResult.Unsupported =>
                PlaybackDecoderFfmpegManagedFrameCopyResult.Unsupported,
            PlaybackDecoderFfmpegFrameDataResult.ContextUnavailable =>
                PlaybackDecoderFfmpegManagedFrameCopyResult.ContextUnavailable,
            PlaybackDecoderFfmpegFrameDataResult.ContextNotOpened =>
                PlaybackDecoderFfmpegManagedFrameCopyResult.ContextNotOpened,
            PlaybackDecoderFfmpegFrameDataResult.GateRejected =>
                PlaybackDecoderFfmpegManagedFrameCopyResult.GateRejected,
            PlaybackDecoderFfmpegFrameDataResult.InvalidMetadata =>
                PlaybackDecoderFfmpegManagedFrameCopyResult.InvalidMetadata,
            PlaybackDecoderFfmpegFrameDataResult.InvalidData =>
                PlaybackDecoderFfmpegManagedFrameCopyResult.InsufficientData,
            PlaybackDecoderFfmpegFrameDataResult.ConsumerRejected =>
                PlaybackDecoderFfmpegManagedFrameCopyResult.ConsumerRejected,
            _ => PlaybackDecoderFfmpegManagedFrameCopyResult.NativeFailure,
        };

    private enum CopyFailure
    {
        None,
        InvalidMetadata,
        InsufficientData,
        AllocationFailed,
        ConsumerRejected,
    }

    private sealed class CopyConsumer(
        IPlaybackDecoderFfmpegManagedFrameConsumer consumer,
        IPlaybackDecoderManagedBufferAllocator allocator) :
        IPlaybackDecoderFfmpegFrameDataConsumer
    {
        private PlaybackDecoderFfmpegFrameMetadata _metadata;
        private PlaneLayout[]? _layouts;
        private byte[][]? _buffers;
        private int _nextPlane;

        public CopyFailure Failure { get; private set; }

        public bool IsComplete { get; private set; }

        public bool Consume(
            PlaybackDecoderFfmpegFrameMetadata metadata,
            PlaybackDecoderFfmpegFrameDataView data)
        {
            if (_layouts is null && !TryInitialize(metadata))
            {
                return false;
            }

            if (metadata != _metadata)
            {
                Failure = CopyFailure.InvalidMetadata;
                Clear(_buffers!);
                return false;
            }

            var layouts = _layouts!;
            var buffers = _buffers!;
            if (_nextPlane >= layouts.Length ||
                data.Index != _nextPlane ||
                data.Kind != layouts[_nextPlane].Kind ||
                data.Rows != layouts[_nextPlane].Rows ||
                data.Stride < layouts[_nextPlane].LogicalRowBytes ||
                !HasRequiredBytes(data))
            {
                Failure = CopyFailure.InsufficientData;
                Clear(buffers);
                return false;
            }

            var layout = layouts[_nextPlane];
            layout.Stride = data.Stride;
            var destination = buffers[_nextPlane];
            for (var row = 0; row < layout.Rows; row++)
            {
                var sourceOffset = checked(row * data.Stride);
                var destinationOffset = checked(row * layout.LogicalRowBytes);
                for (var column = 0; column < layout.LogicalRowBytes; column++)
                {
                    destination[destinationOffset + column] = data.ReadByte(sourceOffset + column);
                }
            }

            _nextPlane++;
            if (_nextPlane != layouts.Length)
            {
                return true;
            }

            PlaybackDecoderFfmpegManagedFrame frame;
            try
            {
                var planes = new PlaybackDecoderFfmpegManagedPlane[layouts.Length];
                for (var index = 0; index < layouts.Length; index++)
                {
                    planes[index] = new(
                        index,
                        layouts[index].Stride,
                        layouts[index].Rows,
                        layouts[index].LogicalRowBytes,
                        buffers[index]);
                }

                frame = new PlaybackDecoderFfmpegManagedFrame(_metadata, planes);
            }
            catch (OutOfMemoryException)
            {
                Clear(buffers);
                Failure = CopyFailure.AllocationFailed;
                return false;
            }

            try
            {
                if (consumer.Consume(frame))
                {
                    IsComplete = true;
                    return true;
                }
            }
            catch (OutOfMemoryException)
            {
                frame.Dispose();
                Failure = CopyFailure.AllocationFailed;
                return false;
            }

            frame.Dispose();
            Failure = CopyFailure.ConsumerRejected;
            return false;
        }

        private bool TryInitialize(PlaybackDecoderFfmpegFrameMetadata metadata)
        {
            PlaneLayout[] layouts;
            try
            {
                if (!TryCreateLayouts(metadata, out layouts))
                {
                    Failure = CopyFailure.InvalidMetadata;
                    return false;
                }
            }
            catch (OutOfMemoryException)
            {
                Failure = CopyFailure.AllocationFailed;
                return false;
            }

            try
            {
                long total = 0;
                var lengths = new int[layouts.Length];
                for (var index = 0; index < layouts.Length; index++)
                {
                    var length = checked(layouts[index].LogicalRowBytes * layouts[index].Rows);
                    total = checked(total + length);
                    if (length < 1 || total > MaximumManagedBytes)
                    {
                        Failure = CopyFailure.InvalidMetadata;
                        return false;
                    }

                    lengths[index] = length;
                }

                var buffers = new byte[layouts.Length][];
                try
                {
                    for (var index = 0; index < layouts.Length; index++)
                    {
                        buffers[index] = allocator.Allocate(lengths[index]);
                        if (buffers[index].Length != lengths[index])
                        {
                            Failure = CopyFailure.AllocationFailed;
                            Clear(buffers);
                            return false;
                        }
                    }
                }
                catch (OutOfMemoryException)
                {
                    Clear(buffers);
                    Failure = CopyFailure.AllocationFailed;
                    return false;
                }

                _metadata = metadata;
                _layouts = layouts;
                _buffers = buffers;
                return true;
            }
            catch (OverflowException)
            {
                Failure = CopyFailure.InvalidMetadata;
                return false;
            }
        }

        private static bool HasRequiredBytes(PlaybackDecoderFfmpegFrameDataView data)
        {
            try
            {
                return data.Length >= checked(data.Stride * data.Rows);
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool TryCreateLayouts(
            PlaybackDecoderFfmpegFrameMetadata metadata,
            out PlaneLayout[] layouts)
        {
            layouts = [];
            if (!IsBoundedMetadata(metadata))
            {
                return false;
            }

            try
            {
                layouts = metadata.Format switch
                {
                    PlaybackDecoderFfmpegFrameFormat.VideoYuv420P =>
                    [
                        new(PlaybackDecoderFfmpegFrameDataKind.VideoPlane, metadata.Width, metadata.Height),
                        new(PlaybackDecoderFfmpegFrameDataKind.VideoPlane, checked((metadata.Width + 1) / 2), checked((metadata.Height + 1) / 2)),
                        new(PlaybackDecoderFfmpegFrameDataKind.VideoPlane, checked((metadata.Width + 1) / 2), checked((metadata.Height + 1) / 2)),
                    ],
                    PlaybackDecoderFfmpegFrameFormat.VideoNv12 =>
                    [
                        new(PlaybackDecoderFfmpegFrameDataKind.VideoPlane, metadata.Width, metadata.Height),
                        new(PlaybackDecoderFfmpegFrameDataKind.VideoPlane, checked(((metadata.Width + 1) / 2) * 2), checked((metadata.Height + 1) / 2)),
                    ],
                    PlaybackDecoderFfmpegFrameFormat.VideoBgra32 =>
                    [new(PlaybackDecoderFfmpegFrameDataKind.VideoPlane, checked(metadata.Width * 4), metadata.Height)],
                    PlaybackDecoderFfmpegFrameFormat.AudioSigned16 =>
                    [new(PlaybackDecoderFfmpegFrameDataKind.AudioSamples, checked(metadata.SampleCount * metadata.ChannelCount * 2), 1)],
                    PlaybackDecoderFfmpegFrameFormat.AudioFloat32 =>
                    [new(PlaybackDecoderFfmpegFrameDataKind.AudioSamples, checked(metadata.SampleCount * metadata.ChannelCount * 4), 1)],
                    PlaybackDecoderFfmpegFrameFormat.AudioSigned16Planar =>
                        CreatePlanar(metadata, 2),
                    PlaybackDecoderFfmpegFrameFormat.AudioFloat32Planar =>
                        CreatePlanar(metadata, 4),
                    _ => [],
                };
                return layouts.Length > 0 && layouts.All(layout =>
                    layout.LogicalRowBytes > 0 && layout.Rows > 0);
            }
            catch (OverflowException)
            {
                layouts = [];
                return false;
            }
        }

        private static bool IsBoundedMetadata(
            PlaybackDecoderFfmpegFrameMetadata metadata)
        {
            var videoFormat = metadata.Format is
                PlaybackDecoderFfmpegFrameFormat.VideoYuv420P or
                PlaybackDecoderFfmpegFrameFormat.VideoNv12 or
                PlaybackDecoderFfmpegFrameFormat.VideoBgra32;
            if (metadata.MediaKind == PlaybackDecoderFfmpegFrameMediaKind.Video)
            {
                return videoFormat &&
                    metadata.Width is >= 1 and <= PlaybackDecodedVideoFrame.MaximumDimension &&
                    metadata.Height is >= 1 and <= PlaybackDecodedVideoFrame.MaximumDimension;
            }

            var audioFormat = metadata.Format is
                PlaybackDecoderFfmpegFrameFormat.AudioSigned16 or
                PlaybackDecoderFfmpegFrameFormat.AudioFloat32 or
                PlaybackDecoderFfmpegFrameFormat.AudioSigned16Planar or
                PlaybackDecoderFfmpegFrameFormat.AudioFloat32Planar;
            return metadata.MediaKind == PlaybackDecoderFfmpegFrameMediaKind.Audio &&
                audioFormat &&
                metadata.SampleRate is >= PlaybackDecodedAudioFrame.MinimumSampleRate and
                    <= PlaybackDecodedAudioFrame.MaximumSampleRate &&
                metadata.ChannelCount is >= PlaybackDecodedAudioFrame.MinimumChannelCount and
                    <= PlaybackDecodedAudioFrame.MaximumChannelCount &&
                metadata.SampleCount is >= 1 and <= PlaybackDecodedAudioFrame.MaximumSampleCount;
        }

        private static PlaneLayout[] CreatePlanar(
            PlaybackDecoderFfmpegFrameMetadata metadata,
            int bytesPerSample) => Enumerable.Range(0, metadata.ChannelCount)
                .Select(_ => new PlaneLayout(
                    PlaybackDecoderFfmpegFrameDataKind.AudioSamples,
                    checked(metadata.SampleCount * bytesPerSample),
                    1))
                .ToArray();

        private static void Clear(byte[][] buffers)
        {
            foreach (var buffer in buffers.Where(buffer => buffer is not null))
            {
                Array.Clear(buffer);
            }
        }

        private sealed class PlaneLayout(
            PlaybackDecoderFfmpegFrameDataKind kind,
            int logicalRowBytes,
            int rows)
        {
            public PlaybackDecoderFfmpegFrameDataKind Kind { get; } = kind;
            public int LogicalRowBytes { get; } = logicalRowBytes;
            public int Rows { get; } = rows;
            public int Stride { get; set; }
        }
    }
}

internal sealed class PlaybackDecoderManagedBufferAllocator :
    IPlaybackDecoderManagedBufferAllocator
{
    public byte[] Allocate(int length) => new byte[length];
}
