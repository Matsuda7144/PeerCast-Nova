using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Media;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4MuxerPcpLoopbackAcceptanceTests
{
    private static readonly Guid ChannelGuid =
        Guid.ParseExact("AF61A25AAB914BB9A47EF4B2A6E1B9C1", "N");
    private static readonly Guid SessionGuid =
        Guid.ParseExact("3DE2B806334B41F79C7A17F4C90DB5F2", "N");

    [Fact]
    public async Task Muxed_segments_traverse_publisher_tcp_pcp_and_receiver_losslessly()
    {
        var fixture = CreateMuxedFixture();
        var channelId = new ChannelId(ChannelGuid);
        var sessionId = new ChannelSessionId(SessionGuid);
        var decoderFactory = new CapturingDecoderFactory(fixture.ExpectedSamples.Count);
        var outputFactory = new NoopPresentationFactory();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var callerStop = new CancellationTokenSource();

        await using var fanout = new PcpRelayFanout(
            new PcpRelayFanoutOptions { MaxPendingPacketsPerChild = 32 },
            replayHeadForNewChildren: true);
        await using var server = new PcpDownstreamRelayServer(
            new IPEndPoint(IPAddress.Loopback, 0),
            fanout,
            channelId,
            SessionGuid);
        var serverTask = server.RunAsync();
        var endpoint = server.LocalEndPoint;
        var sourceUri =
            $"pcp://127.0.0.1:{endpoint.Port}/channel/{ChannelGuid:N}";
        Assert.True(PlaybackPcpFmp4LiveSource.TryParse(sourceUri, out var source));
        await using var runner = new PlaybackPcpFmp4LiveSessionRunner(
            Assert.IsType<PlaybackPcpFmp4LiveSource>(source),
            decoderFactory,
            outputFactory,
            new PlaybackPcpFmp4LiveSessionOptions
            {
                MaximumRunDuration = TimeSpan.FromSeconds(12),
                ConnectTimeout = TimeSpan.FromSeconds(2),
                Session = new PcpChannelSessionOptions
                {
                    HandshakeTimeout = TimeSpan.FromSeconds(3),
                    MaxReconnectAttempts = 0,
                    ReconnectDelay = TimeSpan.Zero,
                },
            });

        var runnerTask = runner.RunAsync(callerStop.Token);
        await WaitForChildRegistrationAsync(fanout, runnerTask, deadline.Token);
        Assert.Equal(
            PcpFmp4PublishingBoundaryCreateResult.Created,
            PcpFmp4PublishingBoundary.Create(
                channelId,
                sessionId,
                fanout,
                out var createdPublisher,
                new PcpFmp4PublishingBoundaryOptions
                {
                    MaximumInitializationBytes = fixture.InitializationSegment.Length,
                    MaximumMediaSegmentBytes = 16 * 1024 * 1024,
                    MaximumPacketPayloadBytes = fixture.InitializationSegment.Length,
                }));
        await using var publisher = Assert.IsType<PcpFmp4PublishingBoundary>(
            createdPublisher);

        Assert.Equal(PcpFmp4PublishingResult.Published,
            await publisher.PublishSegmentAsync(
                fixture.InitializationSegment,
                PcpFmp4PublishingSegmentRole.Initialization,
                deadline.Token));
        foreach (var media in fixture.MediaSegments)
        {
            Assert.Equal(PcpFmp4PublishingResult.Published,
                await publisher.PublishSegmentAsync(
                    media,
                    PcpFmp4PublishingSegmentRole.Media,
                    deadline.Token));
        }

        await decoderFactory.SamplesReceived.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            deadline.Token);
        Assert.True(await fanout.WaitForIdleAsync(
            TimeSpan.FromSeconds(5),
            deadline.Token));
        Assert.Equal(fixture.ExpectedSamples.Count,
            decoderFactory.Samples.Count);

        var publisherSnapshot = publisher.Snapshot;
        var expectedByteCount = fixture.InitializationSegment.Length +
            fixture.MediaSegments.Sum(static media => media.Length);
        var expectedDataPacketCount = fixture.MediaSegments.Sum(media =>
            (media.Length + fixture.InitializationSegment.Length - 1) /
            fixture.InitializationSegment.Length);
        Assert.Equal(1, publisherSnapshot.InitializationSegmentCount);
        Assert.Equal(fixture.MediaSegments.Length,
            publisherSnapshot.MediaSegmentCount);
        Assert.Equal(1, publisherSnapshot.HeadPacketCount);
        Assert.Equal(expectedDataPacketCount, publisherSnapshot.DataPacketCount);
        Assert.Equal(expectedByteCount, publisherSnapshot.AcceptedByteCount);
        Assert.Equal(0, publisherSnapshot.ConsumerFailureCount);

        callerStop.Cancel();
        var runResult = await runnerTask.WaitAsync(deadline.Token);
        Assert.True(runResult.CallerCancelled);
        Assert.Null(runResult.CompletionFailure);
        Assert.Null(runResult.Session);
        Assert.Equal(1, runResult.Composition.InitializationGenerationCount);
        Assert.Equal(1, runResult.Composition.HeadPacketCount);
        Assert.Equal(expectedDataPacketCount, runResult.Composition.DataPacketCount);
        Assert.Equal(fixture.MediaSegments.Length,
            runResult.Composition.MediaSegmentCount);
        Assert.Equal(fixture.MediaSegments.Length, runResult.Composition.BatchCount);
        Assert.Equal(0, runResult.Composition.AssemblerCarryBytes);

        await server.StopAsync(deadline.Token);
        var serverResult = await serverTask.WaitAsync(deadline.Token);
        Assert.Equal(PcpDownstreamRelayServerEndReason.Stopped,
            serverResult.EndReason);
        Assert.Equal(1, serverResult.Snapshot.AcceptedConnectionCount);
        Assert.Equal(1, serverResult.Snapshot.AcceptedHandshakeCount);
        Assert.Equal(publisherSnapshot.AcceptedPacketCount,
            serverResult.Snapshot.DeliveredPacketCount);
        Assert.Equal(expectedByteCount, serverResult.Snapshot.DeliveredByteCount);
        Assert.Equal(0, serverResult.Snapshot.DiscardedPacketCount);
        Assert.Equal(0, serverResult.Snapshot.ProtocolRejectedCount);
        Assert.Equal(0, serverResult.Snapshot.InternalFailureCount);
        Assert.Equal(0, serverResult.Snapshot.CleanupIncompleteCount);
        Assert.Equal(0, serverResult.Snapshot.ActiveConnectionCount);
        Assert.Equal(0, fanout.ActiveChildCount);
        AssertReceivedSamplesEquivalent(fixture.ExpectedSamples, decoderFactory.Samples);
    }

    private static MuxedFixture CreateMuxedFixture()
    {
        var retained = Fmp4PcpLoopbackAcceptanceFixture.Load();
        Assert.Equal(
            PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult.Created,
            PlaybackFmp4LiveEncodedSampleBatchAdapter.Create(
                retained.InitializationSegment,
                out var sourceAdapter));
        var source = Assert.IsType<PlaybackFmp4LiveEncodedSampleBatchAdapter>(
            sourceAdapter);
        var trackDescriptions = source.StreamDescription.TrackDescriptions;
        var tracks = new Fmp4MuxTrackDescription[trackDescriptions.Count];
        for (var index = 0; index < tracks.Length; index++)
        {
            var track = trackDescriptions[index];
            Assert.Equal(1, track.TimeBase.Numerator);
            tracks[index] = new(
                track.TrackId,
                track.MediaRole == PlaybackEncodedMediaRole.Video
                    ? Fmp4MuxMediaKind.Video
                    : Fmp4MuxMediaKind.Audio,
                track.CodecVariant == PlaybackEncodedCodecVariant.Avc1
                    ? Fmp4MuxCodec.Avc1
                    : Fmp4MuxCodec.AacLc,
                checked((uint)track.TimeBase.Denominator),
                track.CodecConfiguration.ToArray(),
                track.Width ?? 0,
                track.Height ?? 0,
                track.SampleRate ?? 0,
                track.ChannelCount ?? 0);
        }

        Assert.Equal(Fmp4MuxerCreateResult.Created,
            Fmp4Muxer.Create(tracks, out var createdMuxer));
        using var muxer = Assert.IsType<Fmp4Muxer>(createdMuxer);
        Assert.Equal(Fmp4MuxerResult.Produced,
            muxer.GenerateInitializationSegment(out var initialization));

        var expected = new List<ExpectedSample>();
        var mediaSegments = new byte[retained.MediaSegments.Length][];
        for (var fragmentIndex = 0; fragmentIndex < mediaSegments.Length; fragmentIndex++)
        {
            Assert.Equal(
                PlaybackEncodedSampleBatchCreationResult.Created,
                source.CreateBatch(retained.MediaSegments[fragmentIndex], out var sourceBatch));
            using var batch = Assert.IsType<PlaybackEncodedSampleBatch>(sourceBatch);
            var muxSamples = new Fmp4MuxEncodedSample[batch.Samples.Count];
            for (var sampleIndex = 0; sampleIndex < muxSamples.Length; sampleIndex++)
            {
                var sourceSample = batch.Samples[sampleIndex];
                var payload = new byte[sourceSample.PayloadSlice.Length];
                batch.CopyPayloadTo(sourceSample, payload);
                var description = trackDescriptions.Single(track =>
                    track.TrackId == sourceSample.TrackId);
                var randomAccess = ToMuxRandomAccess(sourceSample.RandomAccessClassification);
                muxSamples[sampleIndex] = new(
                    sourceSample.TrackId,
                    sourceSample.DtsTicks,
                    sourceSample.PtsTicks,
                    sourceSample.DurationTicks,
                    payload,
                    randomAccess);
                expected.Add(new(
                    sourceSample.TrackId,
                    description.MediaRole,
                    description.CodecKind,
                    description.CodecVariant,
                    description.TimeBase,
                    checked((uint)fragmentIndex + 1),
                    sourceSample.SampleOrdinal,
                    sourceSample.DtsTicks,
                    sourceSample.PtsTicks,
                    sourceSample.DurationTicks,
                    sourceSample.RandomAccessClassification,
                    payload));
            }

            Assert.Equal(Fmp4MuxerResult.Produced,
                muxer.MuxFragment(muxSamples, out var generated));
            var media = Assert.IsType<Fmp4MuxMediaSegment>(generated);
            Assert.Equal((uint)(fragmentIndex + 1), media.SequenceNumber);
            mediaSegments[fragmentIndex] = media.Bytes;
        }

        return new(Assert.IsType<byte[]>(initialization), mediaSegments, expected);
    }

    private static Fmp4MuxRandomAccessKind ToMuxRandomAccess(
        PlaybackEncodedRandomAccessClassification classification) => classification switch
        {
            PlaybackEncodedRandomAccessClassification.IndependentlyDecodable =>
                Fmp4MuxRandomAccessKind.IndependentlyDecodable,
            PlaybackEncodedRandomAccessClassification.NotIndependentlyDecodable =>
                Fmp4MuxRandomAccessKind.NotIndependentlyDecodable,
            PlaybackEncodedRandomAccessClassification.NotApplicable =>
                Fmp4MuxRandomAccessKind.NotApplicable,
            _ => Fmp4MuxRandomAccessKind.Indeterminate,
        };

    private static void AssertReceivedSamplesEquivalent(
        IReadOnlyList<ExpectedSample> expected,
        IReadOnlyList<CapturedSample> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        var expectedOrdered = expected
            .OrderBy(static sample => sample.FragmentSequence)
            .ThenBy(static sample => sample.TrackId)
            .ThenBy(static sample => sample.SampleOrdinal)
            .ToArray();
        var actualOrdered = actual
            .OrderBy(static sample => sample.Metadata.FragmentSequence)
            .ThenBy(static sample => sample.Metadata.TrackId)
            .ThenBy(static sample => sample.Metadata.SampleOrdinal)
            .ToArray();
        for (var index = 0; index < expectedOrdered.Length; index++)
        {
            var source = expectedOrdered[index];
            var received = actualOrdered[index];
            var metadata = received.Metadata;
            Assert.Equal(source.TrackId, metadata.TrackId);
            Assert.Equal(source.Role, metadata.MediaRole);
            Assert.Equal(source.CodecKind, metadata.CodecKind);
            Assert.Equal(source.CodecVariant, metadata.CodecVariant);
            Assert.Equal(source.TimeBase, metadata.TimeBase);
            Assert.Equal(source.FragmentSequence, metadata.FragmentSequence);
            Assert.Equal(source.SampleOrdinal, metadata.SampleOrdinal);
            Assert.Equal(source.DtsTicks, metadata.DtsTicks);
            Assert.Equal(source.PtsTicks, metadata.PtsTicks);
            Assert.Equal(source.DurationTicks, metadata.DurationTicks);
            Assert.Equal(source.RandomAccess, metadata.RandomAccessClassification);
            Assert.Equal(source.Payload, received.Payload);
        }
    }

    private static async Task WaitForChildRegistrationAsync(
        PcpRelayFanout fanout,
        Task runnerTask,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        while (fanout.ActiveChildCount != 1)
        {
            if (runnerTask.IsCompleted)
            {
                await runnerTask.ConfigureAwait(false);
                throw new InvalidOperationException(
                    "The fMP4 receiver stopped before the downstream PCP child subscribed.");
            }

            if (!await timer.WaitForNextTickAsync(cancellationToken)
                    .ConfigureAwait(false))
            {
                throw new TimeoutException(
                    "The downstream PCP child did not register before the deadline.");
            }
        }
    }

    private sealed record MuxedFixture(
        byte[] InitializationSegment,
        byte[][] MediaSegments,
        IReadOnlyList<ExpectedSample> ExpectedSamples);

    private sealed record ExpectedSample(
        uint TrackId,
        PlaybackEncodedMediaRole Role,
        PlaybackEncodedCodecKind CodecKind,
        PlaybackEncodedCodecVariant CodecVariant,
        PlaybackEncodedTimeBase TimeBase,
        uint FragmentSequence,
        int SampleOrdinal,
        long DtsTicks,
        long PtsTicks,
        long DurationTicks,
        PlaybackEncodedRandomAccessClassification RandomAccess,
        byte[] Payload);

    private sealed record CapturedSample(
        PlaybackFmp4SampleMetadata Metadata,
        byte[] Payload);

    private sealed class CapturingDecoderFactory(int expectedSamples) : IPlaybackDecoderFactory
    {
        private readonly TaskCompletionSource _samplesReceived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<CapturedSample> Samples { get; } = [];

        public TaskCompletionSource SamplesReceived => _samplesReceived;

        public IPlaybackDecoder? Create(PlaybackDecoderCreationContext context) =>
            new CapturingDecoder(Samples, _samplesReceived, expectedSamples);
    }

    private sealed class CapturingDecoder(
        List<CapturedSample> samples,
        TaskCompletionSource samplesReceived,
        int expectedSamples) : IPlaybackDecoder
    {
        public ValueTask DecodeAsync(
            PlaybackEncodedInput input,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = input.Fmp4SampleMetadata ?? throw new InvalidOperationException(
                "The PCP receiver did not provide encoded fMP4 sample metadata.");
            samples.Add(new(metadata, input.Payload.ToArray()));
            if (samples.Count >= expectedSamples)
            {
                samplesReceived.TrySetResult();
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask CompleteAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoopPresentationFactory :
        IPlaybackPcpFmp4PresentationOutputGenerationFactory
    {
        public IPlaybackPcpFmp4PresentationOutputGeneration Create(long generation) =>
            new NoopPresentationGeneration();
    }

    private sealed class NoopPresentationGeneration :
        IPlaybackPcpFmp4PresentationOutputGeneration
    {
        public ValueTask WriteAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask WriteVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask CompleteAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
