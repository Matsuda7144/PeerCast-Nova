using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Media;
using PeerCast.Nova.MediaEncoding;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Playback.Tests;

public sealed class LiveEncodingPublisherTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(17)]
    [InlineData(289)]
    public void Source_rejects_unbounded_or_unequal_track_durations(int count) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PacedSyntheticMediaSource(count));

    [Fact]
    public async Task Source_generates_one_owned_event_at_each_absolute_monotonic_target()
    {
        var clock = new ImmediateLiveClock();
        await using var source = new PacedSyntheticMediaSource(16, clock);
        var video = 0; var audio = 0;
        await foreach (var input in source.ReadAllAsync())
        {
            if (input.Video is { } frame)
            {
                Assert.Null(input.Audio); Assert.Equal(video++, frame.PresentationTimestamp);
                var copy = frame.Data.ToArray(); copy[0] ^= 0xff;
                Assert.NotEqual(copy[0], frame.Data.Span[0]);
            }
            else
            {
                Assert.Equal(audio++ * 1024L, input.Audio!.PresentationTimestamp);
                var copy = input.Audio.Samples.ToArray(); copy[0] = 123;
                Assert.NotEqual(123, input.Audio.Samples.Span[0]);
            }
        }
        Assert.Equal(new PacedSyntheticMediaSourceSnapshot(16, 25, true, true), source.Snapshot);
        Assert.Equal(Enumerable.Range(0, 16).Select(i => i * 1600L)
            .Concat(Enumerable.Range(0, 25).Select(i => i * 1024L)).Order().ToArray(), clock.Targets);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        { await foreach (var _ in source.ReadAllAsync()) { } });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_unblocks_source_clock_and_prevents_more_generation(bool dispose)
    {
        var clock = new BlockingClock();
        await using var source = new PacedSyntheticMediaSource(16, clock);
        using var stop = new CancellationTokenSource();
        var reading = Task.Run(async () => { await foreach (var _ in source.ReadAllAsync(stop.Token)) { } });
        await clock.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var before = source.Snapshot;
        if (dispose) { await source.DisposeAsync(); } else { stop.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(before.VideoFrames, source.Snapshot.VideoFrames);
        Assert.Equal(before.AudioBlocks, source.Snapshot.AudioBlocks);
        Assert.True(source.Snapshot.Stopped); Assert.False(source.Snapshot.Completed);
    }

    [Fact]
    public async Task Incremental_fragments_preserve_all_samples_through_production_pcp_wire_and_parser()
    {
        var fake = new FakeLiveEncoder { MutatePreviousBuffers = true };
        var encoder = new CapturingLiveEncoder(fake);
        var decoded = new LiveIdentityDecoderFactory(encoder);
        var output = new LiveDecodedOutput();
        await using var wire = new LivePcpWireLoopback(decoded, output);
        var source = new PacedSyntheticMediaSource(96, new ImmediateLiveClock());
        var fragments = new List<LiveEncodingFragmentSnapshot>();
        var publisher = new LiveEncodingPublisher(source, wire.Channel, wire.Session, wire,
            fragmentPublished: fragments.Add);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await publisher.RunCoreAsync(() => encoder, deadline.Token);
        Assert.True(result.Completed); Assert.False(result.Cancelled);
        Assert.True(result.Source.Completed); Assert.True(result.Source.Stopped);
        Assert.True(fake.Completed); Assert.True(fake.Disposed);
        Assert.Equal(96, result.Video.EncodedPacketCount);
        Assert.Equal(151, result.Audio.EncodedPacketCount); Assert.Equal(2, result.Audio.FlushPacketCount);
        Assert.Equal(1024, result.InitialPaddingSamples);
        Assert.Equal(4, result.Fragments);
        Assert.True(result.FirstPublishVideoFrames < 96); Assert.True(result.FirstPublishAudioBlocks < 150);
        Assert.True(result.SourceRemainingAtFirstPublish > 0);
        Assert.Equal(new uint[] { 1, 2, 3, 4 }, fragments.Select(f => f.Sequence));
        Assert.All(fragments, f => { Assert.True(f.ByteLength > 0); Assert.Equal(64, f.Sha256.Length); });
        Assert.Equal(96, fragments.Sum(f => f.Video?.Samples ?? 0));
        Assert.Equal(151, fragments.Sum(f => f.Audio?.Samples ?? 0));
        Assert.Equal(0, fragments[0].Audio!.FirstDts); Assert.Equal(1024, fragments[0].Video!.FirstDts);
        Assert.Equal(1, result.Publisher.HeadPacketCount);
        Assert.InRange(result.PeakPendingSamples, 1, 128);
        Assert.InRange(result.PeakPendingPayloadBytes, 1, 1024 * 1024);
        var received = await wire.CompleteAsync(deadline.Token);
        Assert.Equal(wire.SentPackets, received.PacketCount);
        Assert.Equal(encoder.Expected.Keys.Order(), decoded.Received.Order());
        Assert.Equal(new uint[] { 1, 2, 3, 4 }, decoded.FragmentSequences);
        Assert.Equal(1, decoded.Completions); Assert.Equal(1, output.Completions);
        Assert.Equal(PlaybackPcpFmp4CompositionState.Completed, wire.Composition.Snapshot.State);
        Assert.Equal(0, wire.Composition.Snapshot.AssemblerCarryBytes);
        Assert.Equal(4, wire.Composition.Snapshot.MediaSegmentCount);
        Assert.Equal(0, wire.Composition.Snapshot.DiscontinuityCount);
        await wire.DisposeAsync();
        Assert.True(decoded.Disposed); Assert.True(output.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Awaited_publish_slows_source_and_cancellation_aborts_without_flush(bool cancel)
    {
        var source = new PacedSyntheticMediaSource(96, new ImmediateLiveClock());
        var encoder = new FakeLiveEncoder();
        var consumer = new BlockingConsumer();
        var publisher = Create(source, consumer);
        using var stop = new CancellationTokenSource();
        var running = publisher.RunCoreAsync(() => encoder, stop.Token);
        await consumer.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var progress = source.Snapshot;
        Assert.InRange(progress.VideoFrames, 1, 95); Assert.InRange(progress.AudioBlocks, 1, 149);
        Assert.False(running.IsCompleted); Assert.False(encoder.Completed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.RunCoreAsync(() => encoder));
        if (cancel) { stop.Cancel(); } else { consumer.Release.TrySetResult(); }
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(!cancel, result.Completed); Assert.Equal(cancel, result.Cancelled);
        Assert.Equal(!cancel, encoder.Completed); Assert.True(encoder.Disposed); Assert.True(source.Snapshot.Stopped);
        if (cancel)
        {
            Assert.Equal(progress.VideoFrames, result.Source.VideoFrames);
            Assert.Equal(progress.AudioBlocks, result.Source.AudioBlocks);
            Assert.Equal(0, result.Audio.FlushPacketCount);
            Assert.Equal(2, consumer.Packets);
            Assert.Equal(PcpFmp4PublishingResult.Cancelled, result.Publisher.Result);
        }
        else { Assert.True(result.SourceRemainingAtFirstPublish > 0); }
    }

    [Fact]
    public async Task Precancelled_start_does_not_create_encoder_and_disposes_owned_source()
    {
        var source = new PacedSyntheticMediaSource(16, new ImmediateLiveClock());
        var created = false;
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(source, new RecordingConsumer())
            .RunCoreAsync(() => { created = true; return new FakeLiveEncoder(); }, stop.Token));
        Assert.False(created); Assert.True(source.Snapshot.Stopped); Assert.Equal(0, source.Snapshot.VideoFrames);
    }

    [Fact]
    public async Task Encoder_failure_propagates_and_disposes_generation()
    {
        var error = new InvalidOperationException("injected encoder failure");
        var encoder = new FakeLiveEncoder { EncodeFailure = error };
        var source = new PacedSyntheticMediaSource(16, new ImmediateLiveClock());
        var consumer = new RecordingConsumer();
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(source, consumer).RunCoreAsync(() => encoder)));
        Assert.True(encoder.Disposed); Assert.False(encoder.Completed); Assert.True(source.Snapshot.Stopped);
        Assert.Single(consumer.Payloads);
    }

    [Fact]
    public async Task Publisher_failure_propagates_canonical_result_and_disposes_generation()
    {
        var encoder = new FakeLiveEncoder();
        var source = new PacedSyntheticMediaSource(96, new ImmediateLiveClock());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(source, new RejectingConsumer()).RunCoreAsync(() => encoder));
        Assert.Contains("ConsumerRejected", error.Message);
        Assert.True(encoder.Disposed); Assert.False(encoder.Completed); Assert.True(source.Snapshot.Stopped);
        Assert.Equal(0, source.Snapshot.VideoFrames); Assert.Equal(0, source.Snapshot.AudioBlocks);
    }

    [Fact]
    public async Task Mux_failure_propagates_without_publishing_invalid_fragment()
    {
        var encoder = new FakeLiveEncoder { InvalidRandomAccess = true };
        var source = new PacedSyntheticMediaSource(96, new ImmediateLiveClock());
        var consumer = new RecordingConsumer();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(source, consumer).RunCoreAsync(() => encoder));
        Assert.Contains("Live mux failed", error.Message);
        Assert.Single(consumer.Payloads); Assert.True(encoder.Disposed); Assert.True(source.Snapshot.Stopped);
    }

    [Fact]
    public async Task Pending_sample_bound_fails_closed_and_releases_encoders()
    {
        var encoder = new FakeLiveEncoder();
        var source = new PacedSyntheticMediaSource(96, new ImmediateLiveClock());
        var consumer = new RecordingConsumer();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(source, consumer, new() { MaximumPendingSamples = 2 }).RunCoreAsync(() => encoder));
        Assert.Contains("pending sample bound", error.Message);
        Assert.True(encoder.Disposed); Assert.False(encoder.Completed); Assert.True(source.Snapshot.Stopped);
        Assert.Single(consumer.Payloads);
    }

    [Fact]
    public async Task One_second_fragment_exceeds_existing_receiver_sample_limit()
    {
        using var encoder = new FakeLiveEncoder();
        await using var source = new PacedSyntheticMediaSource(96, new ImmediateLiveClock());
        var samples = new List<Fmp4MuxEncodedSample>();
        await foreach (var input in source.ReadAllAsync())
        {
            samples.AddRange(encoder.Encode(input).Where(s => s.DtsTicks < 48_000));
            if (source.Snapshot.AudioBlocks > 50 && source.Snapshot.VideoFrames > 30) { break; }
        }
        Assert.Equal(Fmp4MuxerCreateResult.Created, Fmp4Muxer.Create(encoder.Tracks, out var muxer));
        using var ownedMuxer = muxer!;
        Assert.Equal(Fmp4MuxerResult.Produced, ownedMuxer.GenerateInitializationSegment(out var init));
        Assert.Equal(Fmp4MuxerResult.Produced, ownedMuxer.MuxFragment(samples, out var fragment));
        Assert.Equal(PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult.Created,
            PlaybackFmp4LiveEncodedSampleBatchAdapter.Create(init!, out var adapter));
        Assert.Equal(PlaybackEncodedSampleBatchCreationResult.SampleCountLimitExceeded,
            adapter!.CreateBatch(fragment!.Bytes, out _));
    }

    [Fact]
    public async Task Init_is_published_before_source_generation()
    {
        var source = new PacedSyntheticMediaSource(96, new ImmediateLiveClock());
        var first = true;
        var consumer = new RecordingConsumer(packet =>
        {
            if (!first) { return; }
            first = false;
            Assert.Equal(0x64616568U, packet.Type);
            Assert.Equal(0, packet.Sequence);
            Assert.Equal(new PacedSyntheticMediaSourceSnapshot(0, 0, false, false), source.Snapshot);
        });
        Assert.True((await Create(source, consumer).RunCoreAsync(() => new FakeLiveEncoder())).Completed);
        Assert.False(first);
    }

    [Theory]
    [InlineData("DTS")]
    [InlineData("PTS")]
    public async Task Nonmonotonic_encoder_timeline_fails_before_media_publication(string kind)
    {
        var encoder = new FakeLiveEncoder { BadTimeline = kind };
        var source = new PacedSyntheticMediaSource(96, new ImmediateLiveClock());
        var consumer = new RecordingConsumer();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(source, consumer).RunCoreAsync(() => encoder));
        Assert.Contains("contiguous and monotonic", error.Message);
        Assert.Single(consumer.Payloads); Assert.True(encoder.Disposed); Assert.True(source.Snapshot.Stopped);
    }

    [Fact]
    public async Task Consumer_copies_remain_stable_after_later_encode_flush_and_disposal()
    {
        var encoder = new FakeLiveEncoder { MutatePreviousBuffers = true };
        var consumer = new RecordingConsumer();
        var originals = new List<byte[]>();
        var source = new PacedSyntheticMediaSource(96, new ImmediateLiveClock());
        var publisher = new LiveEncodingPublisher(source, new(Guid.NewGuid()), new(Guid.NewGuid()), consumer,
            fragmentPublished: _ => originals.Add(consumer.Payloads[^1].ToArray()));
        var result = await publisher.RunCoreAsync(() => encoder);
        Assert.True(result.Completed);
        Assert.Equal(originals.Count + 1, consumer.Payloads.Count);
        for (var i = 0; i < originals.Count; i++) { Assert.Equal(originals[i], consumer.Payloads[i + 1]); }
    }

    private static LiveEncodingPublisher Create(PacedSyntheticMediaSource source, IPcpChannelPacketConsumer consumer,
        LiveEncodingPublisherOptions? options = null) => new(source, new ChannelId(Guid.NewGuid()),
            new ChannelSessionId(Guid.NewGuid()), consumer, options);

    private sealed class BlockingClock : ILiveMediaClock
    {
        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Start() { }
        public async ValueTask WaitUntilAsync(long mediaTicks, CancellationToken cancellationToken)
        {
            if (mediaTicks > 0) { Blocked.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); }
        }
    }
    private sealed class RecordingConsumer(Action<PcpChannelPacket>? observe = null) : IPcpChannelPacketConsumer
    {
        public List<byte[]> Payloads { get; } = [];
        public ValueTask ConsumeAsync(PcpChannelPacket packet, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); observe?.Invoke(packet); Payloads.Add(packet.Payload.ToArray()); return ValueTask.CompletedTask; }
    }
    private sealed class RejectingConsumer : IPcpChannelPacketConsumer
    {
        public ValueTask ConsumeAsync(PcpChannelPacket packet, CancellationToken cancellationToken) =>
            ValueTask.FromException(new InvalidOperationException("injected consumer failure"));
    }
    private sealed class BlockingConsumer : IPcpChannelPacketConsumer
    {
        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Packets { get; private set; }
        public async ValueTask ConsumeAsync(PcpChannelPacket packet, CancellationToken cancellationToken)
        {
            Packets++;
            if (Packets == 1) { return; }
            Blocked.TrySetResult(); await Release.Task.WaitAsync(cancellationToken);
        }
    }
}
