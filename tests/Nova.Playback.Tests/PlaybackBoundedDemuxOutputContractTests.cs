namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackBoundedDemuxOutputContractTests
{
    [Fact]
    public void TryCreate_OwnsImmutableStreamDescriptionsAndClearsThemOnDispose()
    {
        var videoCodecData = new byte[] { 1, 2, 3 };
        var audioCodecData = new byte[] { 4, 5 };
        var input = CreateDescription(videoCodecData, audioCodecData);

        Assert.Equal(
            PlaybackBoundedDemuxResult.StreamDescriptionAvailable,
            PlaybackBoundedDemuxSession.TryCreate(
                new PlaybackBoundedDemuxOptions(), input, out var session));

        Assert.NotNull(session);
        videoCodecData[0] = 99;
        audioCodecData[0] = 99;
        Assert.Equal(new byte[] { 1, 2, 3 }, session.Video.CodecExtradata.ToArray());
        Assert.Equal(new byte[] { 4, 5 }, session.Audio.CodecExtradata.ToArray());
        Assert.Equal(0, session.Video.StreamId);
        Assert.Equal(1, session.Audio.StreamId);
        Assert.Equal(PlaybackBoundedDemuxContainerKind.Flv, session.ContainerKind);

        var videoView = session.Video.CodecExtradata;
        var audioView = session.Audio.CodecExtradata;
        session.Dispose();
        session.Dispose();

        Assert.True(session.IsDisposed);
        Assert.All(videoView.ToArray(), value => Assert.Equal(0, value));
        Assert.All(audioView.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public void TryCreate_RejectsUnsupportedOrContradictoryDescriptions()
    {
        var duplicateIds = CreateDescription() with
        {
            Audio = CreateDescription().Audio with { StreamId = 0 },
        };
        Assert.Equal(
            PlaybackBoundedDemuxResult.InvalidStreamDescription,
            PlaybackBoundedDemuxSession.TryCreate(
                new PlaybackBoundedDemuxOptions(), duplicateIds, out var duplicateSession));
        Assert.Null(duplicateSession);

        var wrongCodec = CreateDescription() with
        {
            Video = CreateDescription().Video with { Codec = PlaybackBoundedDemuxCodec.Aac },
        };
        Assert.Equal(
            PlaybackBoundedDemuxResult.UnsupportedCodec,
            PlaybackBoundedDemuxSession.TryCreate(
                new PlaybackBoundedDemuxOptions(), wrongCodec, out var codecSession));
        Assert.Null(codecSession);

        var invalidTimeBase = CreateDescription() with
        {
            Audio = CreateDescription().Audio with
            {
                TimeBase = new PlaybackBoundedDemuxTimeBase(0, 48_000),
            },
        };
        Assert.Equal(
            PlaybackBoundedDemuxResult.InvalidStreamDescription,
            PlaybackBoundedDemuxSession.TryCreate(
                new PlaybackBoundedDemuxOptions(), invalidTimeBase, out var timeBaseSession));
        Assert.Null(timeBaseSession);
    }

    [Fact]
    public void TryCreate_EnforcesCodecAndOptionHardLimits()
    {
        var options = new PlaybackBoundedDemuxOptions
        {
            MaximumCodecExtradataBytes = 2,
        };
        Assert.Equal(
            PlaybackBoundedDemuxResult.LimitExceeded,
            PlaybackBoundedDemuxSession.TryCreate(
                options, CreateDescription(new byte[] { 1, 2, 3 }, new byte[] { 4 }), out var session));
        Assert.Null(session);

        var invalidOptions = new PlaybackBoundedDemuxOptions
        {
            MaximumPacketPayloadBytes = 8,
            MaximumOwnedPacketBytesPerRead = 4,
        };
        Assert.Equal(
            PlaybackBoundedDemuxResult.LimitExceeded,
            PlaybackBoundedDemuxSession.TryCreate(
                invalidOptions, CreateDescription(), out var invalidSession));
        Assert.Null(invalidSession);
    }

    [Fact]
    public void TimestampConverter_PreservesPositiveAndNegativeContainerTime()
    {
        var timeBase = new PlaybackBoundedDemuxTimeBase(1, 90_000);

        Assert.Equal(
            PlaybackBoundedDemuxTimestampConversionResult.Converted,
            PlaybackBoundedDemuxTimestampConverter.TryConvert(
                90_000,
                timeBase,
                TimeSpan.FromDays(1),
                out var positive));
        Assert.True(positive.IsKnown);
        Assert.Equal(TimeSpan.FromSeconds(1), positive.Value);

        Assert.Equal(
            PlaybackBoundedDemuxTimestampConversionResult.Converted,
            PlaybackBoundedDemuxTimestampConverter.TryConvert(
                -45_000,
                timeBase,
                TimeSpan.FromDays(1),
                out var negative));
        Assert.Equal(TimeSpan.FromMilliseconds(-500), negative.Value);
    }

    [Fact]
    public void TimestampConverter_FailsClosedForInvalidBaseAndMagnitude()
    {
        Assert.Equal(
            PlaybackBoundedDemuxTimestampConversionResult.InvalidTimeBase,
            PlaybackBoundedDemuxTimestampConverter.TryConvert(
                1,
                new PlaybackBoundedDemuxTimeBase(0, 1),
                TimeSpan.FromSeconds(1),
                out var invalidBase));
        Assert.False(invalidBase.IsKnown);

        Assert.Equal(
            PlaybackBoundedDemuxTimestampConversionResult.MagnitudeExceeded,
            PlaybackBoundedDemuxTimestampConverter.TryConvert(
                long.MaxValue,
                new PlaybackBoundedDemuxTimeBase(1, 1),
                TimeSpan.FromSeconds(1),
                out var excessive));
        Assert.False(excessive.IsKnown);
    }

    [Fact]
    public void CreateReadCompletion_OwnsPacketPayloadAndEnforcesBackpressure()
    {
        using var session = CreateSession();
        var payload = new byte[] { 7, 8, 9 };
        var events = new[]
        {
            Packet(
                session.Video,
                sequence: 0,
                payload,
                pts: PlaybackBoundedDemuxTime.Known(TimeSpan.FromMilliseconds(40)),
                dts: PlaybackBoundedDemuxTime.Known(TimeSpan.Zero),
                keyframe: PlaybackBoundedDemuxKeyframeState.Keyframe),
        };

        using var completion = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            events,
            inputBytesObserved: 32,
            skippedRecordCount: 1);

        Assert.Equal(PlaybackBoundedDemuxResult.PacketsAvailable, completion.Result);
        Assert.Single(completion.Events);
        payload[0] = 99;
        Assert.Equal(new byte[] { 7, 8, 9 }, completion.Events[0].Payload.ToArray());
        Assert.Equal(TimeSpan.FromMilliseconds(40), completion.Events[0].PresentationTimestamp.Value);
        Assert.Equal(TimeSpan.Zero, completion.Events[0].DecodeTimestamp.Value);

        using var blocked = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.NeedMoreInput,
            [],
            inputBytesObserved: 0,
            skippedRecordCount: 0);
        Assert.Equal(PlaybackBoundedDemuxResult.ActiveCompletion, blocked.Result);
    }

    [Fact]
    public void CompletionDisposal_ReleasesGateAndClearsOwnedPayload()
    {
        using var session = CreateSession();
        var completion = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [Packet(session.Audio, 0, new byte[] { 1, 2 })],
            inputBytesObserved: 2,
            skippedRecordCount: 0);
        var view = completion.Events[0].Payload;

        completion.Dispose();
        completion.Dispose();
        Assert.True(completion.IsDisposed);
        Assert.All(view.ToArray(), value => Assert.Equal(0, value));

        using var next = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [Packet(session.Audio, 1, new byte[] { 3 })],
            inputBytesObserved: 1,
            skippedRecordCount: 0);
        Assert.Equal(PlaybackBoundedDemuxResult.PacketsAvailable, next.Result);
    }

    [Fact]
    public void SequenceState_IsMonotonicAcrossFiniteReads()
    {
        using var session = CreateSession();
        using (var first = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [Packet(session.Video, 4, new byte[] { 1 })],
            1,
            0))
        {
            Assert.Equal(PlaybackBoundedDemuxResult.PacketsAvailable, first.Result);
        }

        using var regression = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [Packet(session.Video, 4, new byte[] { 2 })],
            1,
            0);
        Assert.Equal(PlaybackBoundedDemuxResult.MalformedData, regression.Result);
    }

    [Fact]
    public void CreateReadCompletion_EnforcesPacketCountAndAggregateByteLimits()
    {
        var options = new PlaybackBoundedDemuxOptions
        {
            MaximumInputBytesPerRead = 8,
            MaximumPacketPayloadBytes = 4,
            MaximumPacketsPerRead = 1,
            MaximumOwnedPacketBytesPerRead = 4,
        };
        using var session = CreateSession(options);

        using var tooMany = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [
                Packet(session.Video, 0, new byte[] { 1, 2, 3 }),
                Packet(session.Audio, 0, new byte[] { 4, 5, 6 }),
            ],
            inputBytesObserved: 6,
            skippedRecordCount: 0);
        Assert.Equal(PlaybackBoundedDemuxResult.LimitExceeded, tooMany.Result);

        using var oversizedInput = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.NeedMoreInput,
            [],
            inputBytesObserved: 9,
            skippedRecordCount: 0);
        Assert.Equal(PlaybackBoundedDemuxResult.LimitExceeded, oversizedInput.Result);
    }

    [Fact]
    public void NeedMoreInput_RequiresAnEmptyEventSet()
    {
        using var session = CreateSession();

        using var valid = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.NeedMoreInput,
            [],
            inputBytesObserved: 3,
            skippedRecordCount: 0);
        Assert.Equal(PlaybackBoundedDemuxResult.NeedMoreInput, valid.Result);

        using var contradictory = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.NeedMoreInput,
            [Packet(session.Video, 0, new byte[] { 1 })],
            inputBytesObserved: 1,
            skippedRecordCount: 0);
        Assert.Equal(PlaybackBoundedDemuxResult.Faulted, contradictory.Result);
    }

    [Fact]
    public void EndOfStream_IsExplicitPerStreamAndFinalOnlyAfterBothStreamsEnd()
    {
        using var session = CreateSession();
        using (var videoEnd = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [EndOfStream(session.Video, 0)],
            inputBytesObserved: 0,
            skippedRecordCount: 0))
        {
            Assert.Equal(PlaybackBoundedDemuxResult.PacketsAvailable, videoEnd.Result);
            Assert.Equal(PlaybackBoundedDemuxEventKind.EndOfStream, videoEnd.Events[0].Kind);
        }

        using var final = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.EndOfStream,
            [EndOfStream(session.Audio, 0)],
            inputBytesObserved: 0,
            skippedRecordCount: 0);
        Assert.Equal(PlaybackBoundedDemuxResult.EndOfStream, final.Result);
    }

    [Fact]
    public void EndOfStream_RejectsPayloadTimingAndPacketsAfterEnd()
    {
        using var session = CreateSession();
        var invalidEnd = EndOfStream(session.Video, 0) with
        {
            Payload = new byte[] { 1 },
        };
        using var invalid = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [invalidEnd],
            1,
            0);
        Assert.Equal(PlaybackBoundedDemuxResult.MalformedData, invalid.Result);

        using (var ended = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [EndOfStream(session.Video, 0)],
            0,
            0))
        {
            Assert.Equal(PlaybackBoundedDemuxResult.PacketsAvailable, ended.Result);
        }

        using var afterEnd = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [Packet(session.Video, 1, new byte[] { 2 })],
            1,
            0);
        Assert.Equal(PlaybackBoundedDemuxResult.MalformedData, afterEnd.Result);
    }

    [Fact]
    public void PacketValidation_PreservesUnknownTimingAndRejectsInvalidRoles()
    {
        using var session = CreateSession();
        using var unknown = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [Packet(session.Video, 0, new byte[] { 1 })],
            1,
            0);
        Assert.Equal(PlaybackBoundedDemuxResult.PacketsAvailable, unknown.Result);
        Assert.False(unknown.Events[0].PresentationTimestamp.IsKnown);
        unknown.Dispose();

        var invalidAudioKeyframe = Packet(session.Audio, 0, new byte[] { 1 }) with
        {
            KeyframeState = PlaybackBoundedDemuxKeyframeState.Keyframe,
        };
        using var invalid = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [invalidAudioKeyframe],
            1,
            0);
        Assert.Equal(PlaybackBoundedDemuxResult.MalformedData, invalid.Result);
    }

    [Fact]
    public void SourceFailure_RequiresNoOwnedEvents()
    {
        using var session = CreateSession();

        using var truncated = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.TruncatedData,
            [],
            inputBytesObserved: 5,
            skippedRecordCount: 0);
        Assert.Equal(PlaybackBoundedDemuxResult.TruncatedData, truncated.Result);

        using var contradictory = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.TruncatedData,
            [Packet(session.Video, 0, new byte[] { 1 })],
            inputBytesObserved: 5,
            skippedRecordCount: 0);
        Assert.Equal(PlaybackBoundedDemuxResult.Faulted, contradictory.Result);
    }

    [Fact]
    public void CancellationAndUndefinedStatesFailClosedBeforeOwnershipTransfer()
    {
        using var session = CreateSession();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        using var cancelled = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [Packet(session.Video, 0, new byte[] { 1 })],
            1,
            0,
            cancellation.Token);
        Assert.Equal(PlaybackBoundedDemuxResult.Cancelled, cancelled.Result);

        using var undefined = session.CreateReadCompletion(
            (PlaybackBoundedDemuxResult)int.MaxValue,
            [],
            0,
            0);
        Assert.Equal(PlaybackBoundedDemuxResult.Faulted, undefined.Result);
    }

    [Fact]
    public void SessionDisposal_ClearsActivePacketsBeforeCodecData()
    {
        var session = CreateSession();
        var completion = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.PacketsAvailable,
            [Packet(session.Video, 0, new byte[] { 1, 2, 3 })],
            3,
            0);
        var payload = completion.Events[0].Payload;
        var codecData = session.Video.CodecExtradata;

        session.Dispose();

        Assert.True(completion.IsDisposed);
        Assert.All(payload.ToArray(), value => Assert.Equal(0, value));
        Assert.All(codecData.ToArray(), value => Assert.Equal(0, value));
        using var disposed = session.CreateReadCompletion(
            PlaybackBoundedDemuxResult.NeedMoreInput,
            [],
            0,
            0);
        Assert.Equal(PlaybackBoundedDemuxResult.Disposed, disposed.Result);
    }

    private static PlaybackBoundedDemuxSession CreateSession(
        PlaybackBoundedDemuxOptions? options = null)
    {
        Assert.Equal(
            PlaybackBoundedDemuxResult.StreamDescriptionAvailable,
            PlaybackBoundedDemuxSession.TryCreate(
                options ?? new PlaybackBoundedDemuxOptions(),
                CreateDescription(),
                out var session));
        return Assert.IsType<PlaybackBoundedDemuxSession>(session);
    }

    private static PlaybackBoundedDemuxSessionDescriptionInput CreateDescription(
        ReadOnlyMemory<byte>? videoCodecData = null,
        ReadOnlyMemory<byte>? audioCodecData = null) =>
        new(
            PlaybackBoundedDemuxContainerKind.Flv,
            new PlaybackBoundedDemuxStreamDescriptionInput(
                StreamId: 0,
                MediaKind: PlaybackBoundedDemuxMediaKind.Video,
                Codec: PlaybackBoundedDemuxCodec.H264,
                Width: 1280,
                Height: 720,
                SampleRate: 0,
                ChannelCount: 0,
                TimeBase: new PlaybackBoundedDemuxTimeBase(1, 90_000),
                CodecExtradata: videoCodecData ?? new byte[] { 1, 100, 0, 31 }),
            new PlaybackBoundedDemuxStreamDescriptionInput(
                StreamId: 1,
                MediaKind: PlaybackBoundedDemuxMediaKind.Audio,
                Codec: PlaybackBoundedDemuxCodec.Aac,
                Width: 0,
                Height: 0,
                SampleRate: 48_000,
                ChannelCount: 2,
                TimeBase: new PlaybackBoundedDemuxTimeBase(1, 48_000),
                CodecExtradata: audioCodecData ?? new byte[] { 0x11, 0x90 }));

    private static PlaybackBoundedDemuxEventInput Packet(
        PlaybackBoundedDemuxStreamDescription stream,
        long sequence,
        ReadOnlyMemory<byte> payload,
        PlaybackBoundedDemuxTime pts = default,
        PlaybackBoundedDemuxTime dts = default,
        PlaybackBoundedDemuxKeyframeState keyframe =
            PlaybackBoundedDemuxKeyframeState.Unknown) =>
        new(
            PlaybackBoundedDemuxEventKind.Packet,
            stream.StreamId,
            stream.MediaKind,
            sequence,
            payload,
            pts,
            dts,
            PlaybackBoundedDemuxTime.Unknown,
            stream.MediaKind == PlaybackBoundedDemuxMediaKind.Video
                ? keyframe
                : PlaybackBoundedDemuxKeyframeState.NotApplicable,
            PlaybackDecodedFrameDiscontinuityKind.None);

    private static PlaybackBoundedDemuxEventInput EndOfStream(
        PlaybackBoundedDemuxStreamDescription stream,
        long sequence) =>
        new(
            PlaybackBoundedDemuxEventKind.EndOfStream,
            stream.StreamId,
            stream.MediaKind,
            sequence,
            ReadOnlyMemory<byte>.Empty,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxTime.Unknown,
            PlaybackBoundedDemuxKeyframeState.NotApplicable,
            PlaybackDecodedFrameDiscontinuityKind.None);
}
