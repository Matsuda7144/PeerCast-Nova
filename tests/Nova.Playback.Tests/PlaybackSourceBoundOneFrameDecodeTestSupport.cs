namespace PeerCast.Nova.Playback.Tests;

internal static class PlaybackSourceBoundOneFrameDecodeTestSupport
{
    internal static readonly PlaybackBoundedDemuxTimeBase TimeBase = new(1, 1_000);

    internal sealed class Harness : IDisposable
    {
        public Harness(
            int decoderGeneration = 7,
            int discontinuityGeneration = 3,
            PlaybackSourceBoundOneFrameDecodeOptions? options = null)
        {
            Source = CreateSource();
            Log = [];
            PacketOperations = new FakePacketOperations(Log);
            ReceiveOperations = new FakeReceiveOperations();
            NativeSession = new(
                Source,
                0,
                1,
                TimeBase,
                TimeBase,
                new FakeContextOwner(PlaybackDecoderFfmpegCodecId.H264, "video", Log),
                new FakeContextOwner(PlaybackDecoderFfmpegCodecId.Aac, "audio", Log));
            Boundary = new(
                Source,
                NativeSession,
                decoderGeneration,
                discontinuityGeneration,
                options,
                PacketOperations,
                ReceiveOperations);
            DecoderGeneration = decoderGeneration;
            DiscontinuityGeneration = discontinuityGeneration;
        }

        public PlaybackBoundedDemuxSession Source { get; }

        public PlaybackBoundedFlvNativeDecoderSession NativeSession { get; }

        public FakePacketOperations PacketOperations { get; }

        public FakeReceiveOperations ReceiveOperations { get; }

        public PlaybackSourceBoundOneFrameDecodeBoundary Boundary { get; }

        public List<string> Log { get; }

        public int DecoderGeneration { get; }

        public int DiscontinuityGeneration { get; }

        public void Dispose()
        {
            Boundary.Dispose();
            Source.Dispose();
        }
    }

    internal static PlaybackBoundedDemuxSession CreateSource()
    {
        var description = new PlaybackBoundedDemuxSessionDescriptionInput(
            PlaybackBoundedDemuxContainerKind.Flv,
            new PlaybackBoundedDemuxStreamDescriptionInput(
                0,
                PlaybackBoundedDemuxMediaKind.Video,
                PlaybackBoundedDemuxCodec.H264,
                1280,
                720,
                0,
                0,
                TimeBase,
                new byte[] { 1, 100, 0, 31 }),
            new PlaybackBoundedDemuxStreamDescriptionInput(
                1,
                PlaybackBoundedDemuxMediaKind.Audio,
                PlaybackBoundedDemuxCodec.Aac,
                0,
                0,
                48_000,
                2,
                TimeBase,
                new byte[] { 0x11, 0x90 }));
        Assert.Equal(
            PlaybackBoundedDemuxResult.StreamDescriptionAvailable,
            PlaybackBoundedDemuxSession.TryCreate(
                new PlaybackBoundedDemuxOptions(),
                description,
                out var session));
        return Assert.IsType<PlaybackBoundedDemuxSession>(session);
    }

    internal static PlaybackBoundedDemuxReadCompletion CreatePacketCompletion(
        PlaybackBoundedDemuxSession source,
        PlaybackBoundedDemuxMediaKind mediaKind,
        long sequence = 0)
    {
        var completion = source.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [new PlaybackBoundedDemuxEventInput(
                PlaybackBoundedDemuxEventKind.Packet,
                StreamId(mediaKind),
                mediaKind,
                sequence,
                new byte[] { 1, 2, 3 },
                PlaybackBoundedDemuxTime.Known(TimeSpan.FromMilliseconds(120)),
                PlaybackBoundedDemuxTime.Known(TimeSpan.FromMilliseconds(100)),
                PlaybackBoundedDemuxTime.Known(TimeSpan.FromMilliseconds(40)),
                mediaKind == PlaybackBoundedDemuxMediaKind.Video
                    ? PlaybackBoundedDemuxKeyframeState.Keyframe
                    : PlaybackBoundedDemuxKeyframeState.NotApplicable,
                PlaybackDecodedFrameDiscontinuityKind.None)],
            3,
            0);
        Assert.Equal(PlaybackBoundedDemuxResult.PacketsAvailable, completion.Result);
        return completion;
    }

    internal static PlaybackBoundedDemuxReadCompletion CreateEndOfStreamCompletion(
        PlaybackBoundedDemuxSession source,
        PlaybackBoundedDemuxMediaKind mediaKind,
        long sequence = 0)
    {
        var events = new PlaybackBoundedDemuxEventInput[]
        {
            new(
                PlaybackBoundedDemuxEventKind.EndOfStream,
                StreamId(mediaKind),
                mediaKind,
                sequence,
                ReadOnlyMemory<byte>.Empty,
                PlaybackBoundedDemuxTime.Unknown,
                PlaybackBoundedDemuxTime.Unknown,
                PlaybackBoundedDemuxTime.Unknown,
                PlaybackBoundedDemuxKeyframeState.NotApplicable,
                PlaybackDecodedFrameDiscontinuityKind.None),
        };
        var completion = source.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            events,
            0,
            0);
        if (completion.Result == PlaybackBoundedDemuxResult.MalformedData)
        {
            completion.Dispose();
            completion = source.CreateReadCompletion(
                PlaybackBoundedDemuxResult.EndOfStream,
                events,
                0,
                0);
        }

        Assert.True(completion.Result is
            PlaybackBoundedDemuxResult.PacketsAvailable or
            PlaybackBoundedDemuxResult.EndOfStream);
        return completion;
    }

    internal static PlaybackDecoderFfmpegManagedFrame CreateFrame(
        PlaybackBoundedDemuxMediaKind mediaKind,
        int byteCount = 4,
        byte value = 0x5a)
    {
        var metadata = mediaKind == PlaybackBoundedDemuxMediaKind.Video
            ? new PlaybackDecoderFfmpegFrameMetadata(
                PlaybackDecoderFfmpegFrameMediaKind.Video,
                PlaybackDecoderFfmpegFrameFormat.VideoBgra32,
                1,
                1,
                0,
                0,
                0,
                PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(120)),
                PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(100)),
                PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(40)))
            : new PlaybackDecoderFfmpegFrameMetadata(
                PlaybackDecoderFfmpegFrameMediaKind.Audio,
                PlaybackDecoderFfmpegFrameFormat.AudioSigned16,
                0,
                0,
                48_000,
                2,
                1,
                PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(120)),
                PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(120)),
                PlaybackDecoderFfmpegFrameTime.Known(TimeSpan.FromMilliseconds(20)));
        var buffer = Enumerable.Repeat(value, byteCount).ToArray();
        var plane = new PlaybackDecoderFfmpegManagedPlane(
            0,
            byteCount,
            1,
            byteCount,
            buffer);
        return new PlaybackDecoderFfmpegManagedFrame(metadata, [plane]);
    }

    internal static int StreamId(PlaybackBoundedDemuxMediaKind mediaKind) =>
        mediaKind == PlaybackBoundedDemuxMediaKind.Video ? 0 : 1;

    internal sealed class FakeReceiveOperations :
        IPlaybackSourceBoundOneFrameDecodeOperations
    {
        public Queue<PlaybackSourceBoundOneFrameNativeReceive> Results { get; } = new();

        public bool ContextRolesValid { get; set; } = true;

        public int ReceiveCount { get; private set; }

        public int ValidationCount { get; private set; }

        public CancellationTokenSource? CancelDuringReceive { get; set; }

        public bool ValidateContextRole(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxMediaKind mediaKind)
        {
            ValidationCount++;
            return ContextRolesValid;
        }

        public PlaybackSourceBoundOneFrameNativeReceive ReceiveOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxMediaKind mediaKind)
        {
            ReceiveCount++;
            CancelDuringReceive?.Cancel();
            return Results.Count == 0
                ? new(
                    PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput,
                    null)
                : Results.Dequeue();
        }
    }

    internal sealed class FakePacketOperations(
        List<string> log) : IPlaybackFiniteOneEventPacketSubmissionOperations
    {
        public Queue<PlaybackMetadataAwareNativePacketResult> SubmitResults { get; } = new();

        public Queue<PlaybackMetadataAwareNativePacketResult> RetryResults { get; } = new();

        public int SubmitCount { get; private set; }

        public int RetryCount { get; private set; }

        public int ReceiveCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public bool LastRetryWasFlush { get; private set; }

        public PlaybackMetadataAwareNativePacketCompletion SubmitOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxSession sourceSession,
            PlaybackBoundedDemuxReadCompletion sourceCompletion)
        {
            SubmitCount++;
            var result = SubmitResults.Count == 0
                ? PlaybackMetadataAwareNativePacketResult.Submitted
                : SubmitResults.Dequeue();
            var isPacket = sourceCompletion.Events.Count == 1 &&
                sourceCompletion.Events[0].Kind == PlaybackBoundedDemuxEventKind.Packet;
            return new(
                result,
                result == PlaybackMetadataAwareNativePacketResult.NeedFrameDrain && isPacket
                    ? new FakePacketOwner(log)
                    : null,
                sourceCompletion);
        }

        public PlaybackDecoderFfmpegFrameReceiveResult ReceiveOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxMediaKind mediaKind)
        {
            ReceiveCount++;
            return PlaybackDecoderFfmpegFrameReceiveResult.NativeFailure;
        }

        public PlaybackMetadataAwareNativePacketResult RetryOne(
            PlaybackBoundedFlvNativeDecoderSession nativeSession,
            PlaybackBoundedDemuxMediaKind mediaKind,
            bool isFlush)
        {
            RetryCount++;
            LastRetryWasFlush = isFlush;
            return RetryResults.Count == 0
                ? PlaybackMetadataAwareNativePacketResult.Submitted
                : RetryResults.Dequeue();
        }

        public void ReleasePending()
        {
            ReleaseCount++;
            log.Add("submission");
        }
    }

    private sealed class FakePacketOwner(List<string> log) :
        IPlaybackMetadataAwareNativePacketOwner
    {
        private int _isDisposed;

        public PlaybackMetadataAwareNativeSendResult SendOnce(
            PlaybackBoundedFlvNativeDecoderSession session,
            PlaybackBoundedDemuxMediaKind mediaKind) =>
            PlaybackMetadataAwareNativeSendResult.Rejected;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                log.Add("packet");
            }
        }
    }

    private sealed class FakeContextOwner(
        PlaybackDecoderFfmpegCodecId codecId,
        string name,
        List<string> log) : IPlaybackBoundedFlvNativeContextOwner
    {
        private int _isDisposed;

        public PlaybackDecoderFfmpegCodecId CodecId { get; } = codecId;

        public PlaybackDecoderFfmpegContextLease? Context => null;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                log.Add(name);
            }
        }
    }
}
