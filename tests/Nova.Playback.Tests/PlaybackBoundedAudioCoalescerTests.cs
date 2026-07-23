namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackBoundedAudioCoalescerTests
{
    private const int SourceSampleFrames = 1_024;
    private const int SourceBytes =
        SourceSampleFrames * PlaybackBoundedAudioCoalescer.BytesPerSampleFrame;

    [Fact]
    public void Four_source_frames_publish_3840_frames_and_retain_final_256()
    {
        using var coalescer = new PlaybackBoundedAudioCoalescer(
            new() { Generation = 7 });

        Assert.Equal(
            PlaybackBoundedAudioCoalescerResult.Accepting,
            Append(coalescer, sequence: 0, generation: 7, value: 0x11));
        Assert.Equal(
            PlaybackBoundedAudioCoalescerResult.Accepting,
            Append(coalescer, sequence: 1, generation: 7, value: 0x22));
        Assert.Equal(
            PlaybackBoundedAudioCoalescerResult.Accepting,
            Append(coalescer, sequence: 2, generation: 7, value: 0x33));
        Assert.Equal(
            PlaybackBoundedAudioCoalescerResult.BatchReady,
            Append(coalescer, sequence: 3, generation: 7, value: 0x44));

        var ready = coalescer.Snapshot;
        Assert.Equal(3_840, ready.BufferedSampleFrames);
        Assert.Equal(256, ready.RetainedRemainderSampleFrames);
        Assert.Equal(4, ready.AcceptedFrameCount);
        Assert.Equal(32_768, ready.OwnedByteCount);
        Assert.True(ready.HasReadyBatch);

        Assert.True(coalescer.TryTakeReadyBuffer(11, out var buffer));
        Assert.Equal(PlaybackBoundedAudioCoalescer.SampleFormat, buffer.SampleFormat);
        Assert.Equal(PlaybackBoundedAudioCoalescer.SampleRate, buffer.SampleRate);
        Assert.Equal(PlaybackBoundedAudioCoalescer.Channels, buffer.ChannelCount);
        Assert.Equal(3_840, buffer.FrameCount);
        Assert.Equal(30_720, buffer.Buffer.Length);
        Assert.Equal(11, buffer.Generation);

        var bytes = buffer.Buffer.ToArray();
        Assert.All(bytes.AsSpan(0, SourceBytes).ToArray(), value => Assert.Equal(0x11, value));
        Assert.All(
            bytes.AsSpan(SourceBytes, SourceBytes).ToArray(),
            value => Assert.Equal(0x22, value));
        Assert.All(
            bytes.AsSpan(SourceBytes * 2, SourceBytes).ToArray(),
            value => Assert.Equal(0x33, value));
        Assert.All(
            bytes.AsSpan(SourceBytes * 3, 768 *
                PlaybackBoundedAudioCoalescer.BytesPerSampleFrame).ToArray(),
            value => Assert.Equal(0x44, value));

        var carried = coalescer.Snapshot;
        Assert.Equal(256, carried.BufferedSampleFrames);
        Assert.Equal(0, carried.RetainedRemainderSampleFrames);
        Assert.Equal(1, carried.PublishedBatchCount);
        Assert.Equal(30_720, carried.OwnedByteCount);
        Assert.False(carried.HasReadyBatch);

        Assert.Equal(
            PlaybackBoundedAudioCoalescerResult.Accepting,
            Append(coalescer, sequence: 4, generation: 7, value: 0x55));
        Assert.Equal(1_280, coalescer.Snapshot.BufferedSampleFrames);
    }

    [Fact]
    public void Exact_target_frame_publishes_without_remainder()
    {
        using var coalescer = new PlaybackBoundedAudioCoalescer();
        var payload = new byte[3_840 *
            PlaybackBoundedAudioCoalescer.BytesPerSampleFrame];

        var result = coalescer.Append(
            sequence: 0,
            generation: 1,
            PlaybackBoundedAudioCoalescer.SampleFormat,
            PlaybackBoundedAudioCoalescer.SampleRate,
            PlaybackBoundedAudioCoalescer.Channels,
            sampleFrames: 3_840,
            payload);

        Assert.Equal(PlaybackBoundedAudioCoalescerResult.BatchReady, result);
        Assert.Equal(0, coalescer.Snapshot.RetainedRemainderSampleFrames);
        Assert.True(coalescer.TryTakeReadyBuffer(2, out var buffer));
        Assert.Equal(payload, buffer.Buffer.ToArray());
    }

    [Fact]
    public void Ready_batch_blocks_more_input_until_publication()
    {
        using var coalescer = new PlaybackBoundedAudioCoalescer();
        var payload = new byte[3_840 *
            PlaybackBoundedAudioCoalescer.BytesPerSampleFrame];
        Assert.Equal(
            PlaybackBoundedAudioCoalescerResult.BatchReady,
            coalescer.Append(
                sequence: 0,
                generation: 1,
                PlaybackBoundedAudioCoalescer.SampleFormat,
                PlaybackBoundedAudioCoalescer.SampleRate,
                PlaybackBoundedAudioCoalescer.Channels,
                sampleFrames: 3_840,
                payload));

        var before = coalescer.Snapshot;
        Assert.Equal(
            PlaybackBoundedAudioCoalescerResult.BatchPending,
            Append(coalescer, sequence: 1, generation: 1, value: 0x22));
        var after = coalescer.Snapshot;
        Assert.Equal(before.NextSequence, after.NextSequence);
        Assert.Equal(before.AcceptedFrameCount, after.AcceptedFrameCount);
        Assert.Equal(before.BufferedSampleFrames, after.BufferedSampleFrames);
    }

    [Theory]
    [InlineData(-1, 1, PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved, 48_000, 2,
        PlaybackBoundedAudioCoalescerResult.SequenceRejected)]
    [InlineData(1, 1, PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved, 48_000, 2,
        PlaybackBoundedAudioCoalescerResult.SequenceRejected)]
    [InlineData(0, 2, PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved, 48_000, 2,
        PlaybackBoundedAudioCoalescerResult.GenerationRejected)]
    [InlineData(0, 1, PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved, 48_000, 2,
        PlaybackBoundedAudioCoalescerResult.InvalidFrame)]
    [InlineData(0, 1, PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved, 44_100, 2,
        PlaybackBoundedAudioCoalescerResult.InvalidFrame)]
    [InlineData(0, 1, PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved, 48_000, 1,
        PlaybackBoundedAudioCoalescerResult.InvalidFrame)]
    public void Invalid_identity_or_format_is_rejected_without_consuming_sequence(
        long sequence,
        long generation,
        PlaybackDecodedAudioSampleFormat format,
        int sampleRate,
        int channels,
        PlaybackBoundedAudioCoalescerResult expected)
    {
        using var coalescer = new PlaybackBoundedAudioCoalescer();
        var payload = new byte[SourceBytes];

        var result = coalescer.Append(
            sequence,
            generation,
            format,
            sampleRate,
            channels,
            SourceSampleFrames,
            payload);

        Assert.Equal(expected, result);
        Assert.Equal(0, coalescer.Snapshot.NextSequence);
        Assert.Equal(0, coalescer.Snapshot.AcceptedFrameCount);
    }

    [Fact]
    public void Malformed_payload_and_oversized_source_frame_are_rejected()
    {
        using var coalescer = new PlaybackBoundedAudioCoalescer();

        Assert.Equal(
            PlaybackBoundedAudioCoalescerResult.InvalidFrame,
            coalescer.Append(
                sequence: 0,
                generation: 1,
                PlaybackBoundedAudioCoalescer.SampleFormat,
                PlaybackBoundedAudioCoalescer.SampleRate,
                PlaybackBoundedAudioCoalescer.Channels,
                SourceSampleFrames,
                new byte[SourceBytes - 1]));
        Assert.Equal(
            PlaybackBoundedAudioCoalescerResult.InvalidFrame,
            coalescer.Append(
                sequence: 0,
                generation: 1,
                PlaybackBoundedAudioCoalescer.SampleFormat,
                PlaybackBoundedAudioCoalescer.SampleRate,
                PlaybackBoundedAudioCoalescer.Channels,
                sampleFrames: 3_841,
                new byte[3_841 *
                    PlaybackBoundedAudioCoalescer.BytesPerSampleFrame]));
        Assert.Equal(0, coalescer.Snapshot.NextSequence);
    }

    [Fact]
    public void Cancellation_does_not_mutate_owned_audio()
    {
        using var coalescer = new PlaybackBoundedAudioCoalescer();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = coalescer.Append(
            sequence: 0,
            generation: 1,
            PlaybackBoundedAudioCoalescer.SampleFormat,
            PlaybackBoundedAudioCoalescer.SampleRate,
            PlaybackBoundedAudioCoalescer.Channels,
            SourceSampleFrames,
            new byte[SourceBytes],
            cancellation.Token);

        Assert.Equal(PlaybackBoundedAudioCoalescerResult.Cancelled, result);
        Assert.Equal(0, coalescer.Snapshot.NextSequence);
        Assert.Equal(0, coalescer.Snapshot.BufferedSampleFrames);
    }

    [Fact]
    public void Constructor_rejects_unbounded_or_insufficient_ownership_budget()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlaybackBoundedAudioCoalescer(
                new() { TargetSampleFrames = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlaybackBoundedAudioCoalescer(
                new()
                {
                    MaximumOwnedBytes =
                        PlaybackBoundedAudioCoalescerOptions.DefaultTargetSampleFrames *
                        PlaybackBoundedAudioCoalescer.BytesPerSampleFrame - 1,
                }));
    }

    [Fact]
    public void Dispose_releases_all_owned_payload_and_rejects_future_input()
    {
        var coalescer = new PlaybackBoundedAudioCoalescer();
        Assert.Equal(
            PlaybackBoundedAudioCoalescerResult.Accepting,
            Append(coalescer, sequence: 0, generation: 1, value: 0x11));

        coalescer.Dispose();

        var snapshot = coalescer.Snapshot;
        Assert.True(snapshot.IsDisposed);
        Assert.Equal(0, snapshot.OwnedByteCount);
        Assert.Equal(
            PlaybackBoundedAudioCoalescerResult.Disposed,
            Append(coalescer, sequence: 1, generation: 1, value: 0x22));
        Assert.False(coalescer.TryTakeReadyBuffer(1, out _));
    }

    private static PlaybackBoundedAudioCoalescerResult Append(
        PlaybackBoundedAudioCoalescer coalescer,
        long sequence,
        long generation,
        byte value)
    {
        var payload = new byte[SourceBytes];
        Array.Fill(payload, value);
        return coalescer.Append(
            sequence,
            generation,
            PlaybackBoundedAudioCoalescer.SampleFormat,
            PlaybackBoundedAudioCoalescer.SampleRate,
            PlaybackBoundedAudioCoalescer.Channels,
            SourceSampleFrames,
            payload);
    }
}
