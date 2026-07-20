using System.Runtime.InteropServices;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackMetadataAwareNativePacketBoundaryTests
{
    private static readonly PlaybackBoundedDemuxTimeBase TimeBase = new(1, 1_000);

    [Fact]
    public void SubmitOne_maps_exact_metadata_and_cleans_packet_before_source_completion()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations
        {
            OwnerDisposeProbe = () =>
            {
                using var probe = source.CreateReadCompletion(
                    PlaybackBoundedDemuxResult.NeedMoreInput,
                    [],
                    0,
                    0);
                return probe.Result == PlaybackBoundedDemuxResult.ActiveCompletion;
            },
        };
        var completion = CreatePacketCompletion(
            source,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackBoundedDemuxTime.Known(TimeSpan.FromMilliseconds(120)),
            PlaybackBoundedDemuxTime.Known(TimeSpan.FromMilliseconds(100)),
            PlaybackBoundedDemuxTime.Known(TimeSpan.FromMilliseconds(40)),
            PlaybackBoundedDemuxKeyframeState.Keyframe);

        var result = new PlaybackMetadataAwareNativePacketBoundary(operations)
            .SubmitOne(session, source, completion);

        Assert.Equal(PlaybackMetadataAwareNativePacketResult.Submitted, result.Result);
        Assert.True(completion.IsDisposed);
        Assert.False(result.RetainsPendingOwnership);
        Assert.Equal(120, operations.Input?.PresentationTimestamp);
        Assert.Equal(100, operations.Input?.DecodeTimestamp);
        Assert.Equal(40, operations.Input?.Duration);
        Assert.Equal(0, operations.Input?.StreamIndex);
        Assert.Equal(1, operations.Input?.Flags);
        Assert.Equal(TimeBase, operations.Input?.TimeBase);
        Assert.Equal(1, operations.SendCount);
        Assert.Equal(1, operations.OwnerDisposeCount);
        Assert.True(operations.SourceCompletionWasActiveDuringOwnerDispose);
    }

    [Fact]
    public void SubmitOne_maps_unknown_values_without_synthesis()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations();
        var completion = CreatePacketCompletion(
            source,
            PlaybackBoundedDemuxMediaKind.Audio,
            1,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxKeyframeState.NotApplicable);

        var result = new PlaybackMetadataAwareNativePacketBoundary(operations)
            .SubmitOne(session, source, completion);

        Assert.Equal(PlaybackMetadataAwareNativePacketResult.Submitted, result.Result);
        Assert.Equal(long.MinValue, operations.Input?.PresentationTimestamp);
        Assert.Equal(long.MinValue, operations.Input?.DecodeTimestamp);
        Assert.Equal(0, operations.Input?.Duration);
        Assert.Equal(0, operations.Input?.Flags);
        Assert.Equal(PlaybackBoundedDemuxMediaKind.Audio, operations.MediaKind);
    }

    [Fact]
    public void SubmitOne_retains_native_packet_and_source_backpressure_on_again()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations
        {
            SendResult = PlaybackMetadataAwareNativeSendResult.Again,
        };
        var completion = CreatePacketCompletion(
            source,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxKeyframeState.Keyframe);

        var result = new PlaybackMetadataAwareNativePacketBoundary(operations)
            .SubmitOne(session, source, completion);

        Assert.Equal(PlaybackMetadataAwareNativePacketResult.NeedFrameDrain, result.Result);
        Assert.True(result.RetainsPendingOwnership);
        Assert.False(completion.IsDisposed);
        Assert.Equal(0, operations.OwnerDisposeCount);
        using (var blocked = source.CreateReadCompletion(
            PlaybackBoundedDemuxResult.NeedMoreInput,
            [],
            0,
            0))
        {
            Assert.Equal(PlaybackBoundedDemuxResult.ActiveCompletion, blocked.Result);
        }

        result.Dispose();
        result.Dispose();

        Assert.True(result.IsDisposed);
        Assert.True(completion.IsDisposed);
        Assert.Equal(1, operations.OwnerDisposeCount);
        using var released = source.CreateReadCompletion(
            PlaybackBoundedDemuxResult.NeedMoreInput,
            [],
            0,
            0);
        Assert.Equal(PlaybackBoundedDemuxResult.NeedMoreInput, released.Result);
    }

    [Fact]
    public void SubmitOne_accepts_first_per_stream_eos_from_packets_available_completion()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations();
        var completion = CreateEndOfStreamCompletion(
            source,
            PlaybackBoundedDemuxResult.PacketsAvailable,
            PlaybackBoundedDemuxMediaKind.Video,
            0);

        var result = new PlaybackMetadataAwareNativePacketBoundary(operations)
            .SubmitOne(session, source, completion);

        Assert.Equal(PlaybackMetadataAwareNativePacketResult.Submitted, result.Result);
        Assert.Equal(1, operations.FlushCount);
        Assert.Equal(0, operations.CreateCount);
        Assert.Equal(PlaybackBoundedDemuxMediaKind.Video, operations.MediaKind);
        Assert.True(completion.IsDisposed);
    }

    [Fact]
    public void SubmitOne_retains_per_stream_eos_completion_when_flush_needs_drain()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations
        {
            FlushResult = PlaybackMetadataAwareNativeSendResult.Again,
        };
        var completion = CreateEndOfStreamCompletion(
            source,
            PlaybackBoundedDemuxResult.PacketsAvailable,
            PlaybackBoundedDemuxMediaKind.Video,
            0);

        var result = new PlaybackMetadataAwareNativePacketBoundary(operations)
            .SubmitOne(session, source, completion);

        Assert.Equal(PlaybackMetadataAwareNativePacketResult.NeedFrameDrain, result.Result);
        Assert.True(result.RetainsPendingOwnership);
        Assert.False(completion.IsDisposed);
        using (var blocked = source.CreateReadCompletion(
            PlaybackBoundedDemuxResult.NeedMoreInput,
            [],
            0,
            0))
        {
            Assert.Equal(PlaybackBoundedDemuxResult.ActiveCompletion, blocked.Result);
        }

        result.Dispose();

        Assert.True(completion.IsDisposed);
        using var released = source.CreateReadCompletion(
            PlaybackBoundedDemuxResult.NeedMoreInput,
            [],
            0,
            0);
        Assert.Equal(PlaybackBoundedDemuxResult.NeedMoreInput, released.Result);
    }

    [Fact]
    public void SubmitOne_maps_final_stream_eos_native_eof()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations();
        var videoEos = CreateEndOfStreamCompletion(
            source,
            PlaybackBoundedDemuxResult.PacketsAvailable,
            PlaybackBoundedDemuxMediaKind.Video,
            0);
        var first = new PlaybackMetadataAwareNativePacketBoundary(operations)
            .SubmitOne(session, source, videoEos);
        Assert.Equal(PlaybackMetadataAwareNativePacketResult.Submitted, first.Result);

        operations.FlushResult = PlaybackMetadataAwareNativeSendResult.EndOfStream;
        var audioEos = CreateEndOfStreamCompletion(
            source,
            PlaybackBoundedDemuxResult.EndOfStream,
            PlaybackBoundedDemuxMediaKind.Audio,
            1);

        var result = new PlaybackMetadataAwareNativePacketBoundary(operations)
            .SubmitOne(session, source, audioEos);

        Assert.Equal(PlaybackMetadataAwareNativePacketResult.EndOfStream, result.Result);
        Assert.Equal(2, operations.FlushCount);
        Assert.True(audioEos.IsDisposed);
    }

    [Fact]
    public void SubmitOne_rejects_wrong_source_before_native_work()
    {
        using var source = CreateSource();
        using var wrongSource = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations();
        var completion = CreatePacketCompletion(
            wrongSource,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxKeyframeState.Keyframe);

        var result = new PlaybackMetadataAwareNativePacketBoundary(operations)
            .SubmitOne(session, wrongSource, completion);

        Assert.Equal(PlaybackMetadataAwareNativePacketResult.SourceMismatch, result.Result);
        Assert.True(completion.IsDisposed);
        Assert.Equal(0, operations.CreateCount);
    }

    [Fact]
    public void SubmitOne_rejects_native_session_time_base_mismatch()
    {
        using var source = CreateSource();
        using var session = CreateSession(source, videoTimeBase: new(1, 90_000));
        var operations = new FakeOperations();
        var completion = CreatePacketCompletion(
            source,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxKeyframeState.Keyframe);

        var result = new PlaybackMetadataAwareNativePacketBoundary(operations)
            .SubmitOne(session, source, completion);

        Assert.Equal(PlaybackMetadataAwareNativePacketResult.EventRejected, result.Result);
        Assert.True(completion.IsDisposed);
        Assert.Equal(0, operations.CreateCount);
    }

    [Fact]
    public void SubmitOne_rejects_non_integral_timestamp_mapping()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations();
        var completion = CreatePacketCompletion(
            source,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackBoundedDemuxTime.Known(TimeSpan.FromTicks(1)),
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxKeyframeState.Keyframe);

        var result = new PlaybackMetadataAwareNativePacketBoundary(operations)
            .SubmitOne(session, source, completion);

        Assert.Equal(PlaybackMetadataAwareNativePacketResult.TimestampRejected, result.Result);
        Assert.True(completion.IsDisposed);
        Assert.Equal(0, operations.CreateCount);
    }

    [Fact]
    public void SubmitOne_rejects_negative_duration_without_native_work()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations();
        var completion = CreateDetachedPacketCompletion(
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            new byte[] { 1, 2, 3 },
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Known(TimeSpan.FromMilliseconds(-1)),
            PlaybackBoundedDemuxKeyframeState.Keyframe);

        var result = new PlaybackMetadataAwareNativePacketBoundary(operations)
            .SubmitOne(session, source, completion);

        Assert.Equal(PlaybackMetadataAwareNativePacketResult.TimestampRejected, result.Result);
        Assert.Equal(0, operations.CreateCount);
        Assert.True(completion.IsDisposed);
    }

    [Fact]
    public void SubmitOne_rejects_payload_above_native_boundary_limit()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations();
        var completion = CreateDetachedPacketCompletion(
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            new byte[PlaybackMetadataAwareNativePacketBoundary.MaximumPacketBytes + 1],
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxKeyframeState.Keyframe);

        var result = new PlaybackMetadataAwareNativePacketBoundary(operations)
            .SubmitOne(session, source, completion);

        Assert.Equal(PlaybackMetadataAwareNativePacketResult.PacketTooLarge, result.Result);
        Assert.Equal(0, operations.CreateCount);
        Assert.True(completion.IsDisposed);
    }

    [Fact]
    public void SubmitOne_fails_closed_on_undefined_native_result()
    {
        using var source = CreateSource();
        using var session = CreateSession(source);
        var operations = new FakeOperations
        {
            CreationResult = (PlaybackMetadataAwareNativeSendResult)999,
        };
        var completion = CreatePacketCompletion(
            source,
            PlaybackBoundedDemuxMediaKind.Video,
            0,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxKeyframeState.Keyframe);

        var result = new PlaybackMetadataAwareNativePacketBoundary(operations)
            .SubmitOne(session, source, completion);

        Assert.Equal(PlaybackMetadataAwareNativePacketResult.NativeRejected, result.Result);
        Assert.True(completion.IsDisposed);
    }

    [Fact]
    public void Native_payload_writer_copies_payload_and_zeroes_required_padding()
    {
        var payload = new byte[] { 4, 5, 6 };
        var byteCount = payload.Length + WindowsPlaybackMetadataAwareNativePacketOwner.InputPadding;
        var memory = Marshal.AllocHGlobal(byteCount);
        try
        {
            for (var index = 0; index < byteCount; index++)
            {
                Marshal.WriteByte(memory, index, 0xff);
            }

            WindowsPlaybackMetadataAwareNativePacketOwner.WritePayloadAndPadding(memory, payload);

            for (var index = 0; index < payload.Length; index++)
            {
                Assert.Equal(payload[index], Marshal.ReadByte(memory, index));
            }

            for (var index = payload.Length; index < byteCount; index++)
            {
                Assert.Equal(0, Marshal.ReadByte(memory, index));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    [Fact]
    public void Native_metadata_writer_uses_reviewed_ffmpeg8_x64_prefix_offsets()
    {
        const int packetBytes = 104;
        var memory = Marshal.AllocHGlobal(packetBytes);
        try
        {
            for (var index = 0; index < packetBytes; index++)
            {
                Marshal.WriteByte(memory, index, 0);
            }

            var input = new PlaybackMetadataAwareNativePacketInput(
                new byte[] { 1 },
                120,
                100,
                40,
                7,
                1,
                new PlaybackBoundedDemuxTimeBase(1, 1_000));
            WindowsPlaybackMetadataAwareNativePacketOwner.WriteReviewedMetadata(memory, input);

            Assert.Equal(120, Marshal.ReadInt64(memory, 8));
            Assert.Equal(100, Marshal.ReadInt64(memory, 16));
            Assert.Equal(7, Marshal.ReadInt32(memory, 36));
            Assert.Equal(1, Marshal.ReadInt32(memory, 40));
            Assert.Equal(40, Marshal.ReadInt64(memory, 64));
            Assert.Equal(1, Marshal.ReadInt32(memory, 96));
            Assert.Equal(1_000, Marshal.ReadInt32(memory, 100));
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    private static PlaybackBoundedDemuxSession CreateSource()
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

    private static PlaybackBoundedFlvNativeDecoderSession CreateSession(
        PlaybackBoundedDemuxSession source,
        PlaybackBoundedDemuxTimeBase? videoTimeBase = null,
        PlaybackBoundedDemuxTimeBase? audioTimeBase = null) =>
        new(
            source,
            0,
            1,
            videoTimeBase ?? TimeBase,
            audioTimeBase ?? TimeBase,
            new FakeContextOwner(PlaybackDecoderFfmpegCodecId.H264),
            new FakeContextOwner(PlaybackDecoderFfmpegCodecId.Aac));

    private static PlaybackBoundedDemuxReadCompletion CreatePacketCompletion(
        PlaybackBoundedDemuxSession source,
        PlaybackBoundedDemuxMediaKind mediaKind,
        int streamId,
        PlaybackBoundedDemuxTime pts,
        PlaybackBoundedDemuxTime dts,
        PlaybackBoundedDemuxTime duration,
        PlaybackBoundedDemuxKeyframeState keyframeState)
    {
        var input = new PlaybackBoundedDemuxEventInput(
            PlaybackBoundedDemuxEventKind.Packet,
            streamId,
            mediaKind,
            0,
            new byte[] { 1, 2, 3 },
            pts,
            dts,
            duration,
            keyframeState,
            PlaybackDecodedFrameDiscontinuityKind.None);
        var completion = source.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [input],
            input.Payload.Length,
            0);
        Assert.Equal(PlaybackBoundedDemuxResult.PacketsAvailable, completion.Result);
        return completion;
    }

    private static PlaybackBoundedDemuxReadCompletion CreateEndOfStreamCompletion(
        PlaybackBoundedDemuxSession source,
        PlaybackBoundedDemuxResult sourceResult,
        PlaybackBoundedDemuxMediaKind mediaKind,
        int streamId)
    {
        var input = new PlaybackBoundedDemuxEventInput(
            PlaybackBoundedDemuxEventKind.EndOfStream,
            streamId,
            mediaKind,
            0,
            ReadOnlyMemory<byte>.Empty,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxKeyframeState.NotApplicable,
            PlaybackDecodedFrameDiscontinuityKind.None);
        var completion = source.CreateReadCompletion(sourceResult, [input], 0, 0);
        Assert.Equal(sourceResult, completion.Result);
        return completion;
    }

    private static PlaybackBoundedDemuxReadCompletion CreateDetachedPacketCompletion(
        PlaybackBoundedDemuxMediaKind mediaKind,
        int streamId,
        byte[] payload,
        PlaybackBoundedDemuxTime pts,
        PlaybackBoundedDemuxTime dts,
        PlaybackBoundedDemuxTime duration,
        PlaybackBoundedDemuxKeyframeState keyframeState)
    {
        var input = new PlaybackBoundedDemuxEventInput(
            PlaybackBoundedDemuxEventKind.Packet,
            streamId,
            mediaKind,
            0,
            payload,
            pts,
            dts,
            duration,
            keyframeState,
            PlaybackDecodedFrameDiscontinuityKind.None);
        return new(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [new PlaybackBoundedDemuxEvent(input, payload)],
            payload.Length,
            0);
    }

    private sealed class FakeContextOwner(PlaybackDecoderFfmpegCodecId codecId) :
        IPlaybackBoundedFlvNativeContextOwner
    {
        public PlaybackDecoderFfmpegCodecId CodecId { get; } = codecId;

        public PlaybackDecoderFfmpegContextLease? Context => null;

        public void Dispose()
        {
        }
    }

    private sealed class FakePacketOwner(FakeOperations operations) :
        IPlaybackMetadataAwareNativePacketOwner
    {
        private int _disposed;

        public PlaybackMetadataAwareNativeSendResult SendOnce(
            PlaybackBoundedFlvNativeDecoderSession session,
            PlaybackBoundedDemuxMediaKind mediaKind)
        {
            operations.SendCount++;
            return operations.SendResult;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                operations.SourceCompletionWasActiveDuringOwnerDispose =
                    operations.OwnerDisposeProbe?.Invoke();
                operations.OwnerDisposeCount++;
            }
        }
    }

    private sealed class FakeOperations : IPlaybackMetadataAwareNativePacketOperations
    {
        public PlaybackMetadataAwareNativeSendResult CreationResult { get; init; } =
            PlaybackMetadataAwareNativeSendResult.Success;

        public PlaybackMetadataAwareNativeSendResult SendResult { get; init; } =
            PlaybackMetadataAwareNativeSendResult.Success;

        public PlaybackMetadataAwareNativeSendResult FlushResult { get; set; } =
            PlaybackMetadataAwareNativeSendResult.Success;

        public PlaybackMetadataAwareNativePacketInput? Input { get; private set; }

        public PlaybackBoundedDemuxMediaKind? MediaKind { get; private set; }

        public Func<bool>? OwnerDisposeProbe { get; init; }

        public bool? SourceCompletionWasActiveDuringOwnerDispose { get; set; }

        public int CreateCount { get; private set; }

        public int SendCount { get; set; }

        public int FlushCount { get; private set; }

        public int OwnerDisposeCount { get; set; }

        public PlaybackMetadataAwareNativePacketCreation CreatePacket(
            PlaybackBoundedFlvNativeDecoderSession session,
            PlaybackBoundedDemuxMediaKind mediaKind,
            PlaybackMetadataAwareNativePacketInput input)
        {
            CreateCount++;
            Input = input;
            MediaKind = mediaKind;
            return CreationResult == PlaybackMetadataAwareNativeSendResult.Success
                ? new(CreationResult, new FakePacketOwner(this))
                : new(CreationResult, null);
        }

        public PlaybackMetadataAwareNativeSendResult SendFlush(
            PlaybackBoundedFlvNativeDecoderSession session,
            PlaybackBoundedDemuxMediaKind mediaKind)
        {
            FlushCount++;
            MediaKind = mediaKind;
            return FlushResult;
        }
    }
}
