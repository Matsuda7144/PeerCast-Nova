namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecodedAudioSliceCollectorTests
{
    [Fact]
    public void Three_full_frames_and_768_samples_from_fourth_are_copied_in_order()
    {
        using var collector = new PlaybackDecodedAudioSliceCollector();
        var frames = new[]
        {
            Payload(1, 1_024),
            Payload(2, 1_024),
            Payload(3, 1_024),
            PayloadWithExcludedTail(),
        };

        for (var sequence = 0; sequence < frames.Length; sequence++)
        {
            Assert.True(Append(collector, sequence, frames[sequence], out _));
        }

        var snapshot = collector.Snapshot;
        Assert.Equal(PlaybackDecodedAudioSliceCollectorResult.Completed, snapshot.Result);
        Assert.Equal(4, snapshot.DecodedFramesConsumed);
        Assert.Equal(3, snapshot.FullDecodedFramesCopied);
        Assert.Equal(768, snapshot.FinalFrameSampleFramesCopied);
        Assert.Equal(3_840, snapshot.OutputSampleFrames);
        Assert.Equal(30_720, snapshot.PayloadBytes);
        Assert.False(snapshot.PayloadPublished);
        Assert.True(collector.TryTakeCompletedBuffer(7, out var output));
        Assert.Equal(7, output.Generation);
        Assert.Equal(30_720, output.Buffer.Length);

        var expected = frames[0]
            .Concat(frames[1])
            .Concat(frames[2])
            .Concat(frames[3].Take(768 * 2 * sizeof(float)))
            .ToArray();
        Assert.Equal(expected, output.Buffer.ToArray());
        Assert.DoesNotContain((byte)5, output.Buffer.ToArray());
    }

    [Fact]
    public void Stereo_interleaving_is_preserved()
    {
        using var collector = new PlaybackDecodedAudioSliceCollector();
        for (var sequence = 0; sequence < 4; sequence++)
        {
            var payload = new byte[1_024 * 2 * sizeof(float)];
            for (var sample = 0; sample < 1_024; sample++)
            {
                BitConverter.GetBytes(sequence + (sample / 10_000F))
                    .CopyTo(payload, sample * 8);
                BitConverter.GetBytes(-(sequence + (sample / 10_000F)))
                    .CopyTo(payload, (sample * 8) + 4);
            }

            Assert.True(Append(collector, sequence, payload, out _));
        }

        Assert.True(collector.TryTakeCompletedBuffer(1, out var output));
        var span = output.Buffer.Span;
        for (var frame = 0; frame < 3_840; frame++)
        {
            var left = BitConverter.ToSingle(span.Slice(frame * 8, 4));
            var right = BitConverter.ToSingle(span.Slice((frame * 8) + 4, 4));
            Assert.Equal(-left, right);
        }
    }

    [Theory]
    [InlineData(PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved, 48000, 2)]
    [InlineData(PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved, 44100, 2)]
    [InlineData(PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved, 48000, 1)]
    public void Format_rate_or_channel_mismatch_is_rejected(
        PlaybackDecodedAudioSampleFormat format,
        int rate,
        int channels)
    {
        using var collector = new PlaybackDecodedAudioSliceCollector();
        var sampleFrames = 1_024;
        var bytesPerSample = format ==
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved ? 2 : 4;
        var payload = new byte[sampleFrames * channels * bytesPerSample];

        Assert.False(collector.TryAppend(
            sequence: 0,
            generation: 1,
            format,
            rate,
            channels,
            sampleFrames,
            payload,
            CancellationToken.None,
            out var result));
        Assert.Equal(PlaybackDecodedAudioSliceCollectorResult.InvalidFrame, result);
        Assert.False(collector.Snapshot.PayloadPublished);
    }

    [Fact]
    public void Generation_and_sequence_mismatch_fail_closed()
    {
        using var generation = new PlaybackDecodedAudioSliceCollector();
        Assert.False(generation.TryAppend(
            0, 2, PlaybackDecodedAudioSliceCollector.SampleFormat,
            48_000, 2, 1_024, Payload(1, 1_024), CancellationToken.None,
            out var generationResult));
        Assert.Equal(
            PlaybackDecodedAudioSliceCollectorResult.GenerationRejected,
            generationResult);

        using var sequence = new PlaybackDecodedAudioSliceCollector();
        Assert.False(sequence.TryAppend(
            1, 1, PlaybackDecodedAudioSliceCollector.SampleFormat,
            48_000, 2, 1_024, Payload(1, 1_024), CancellationToken.None,
            out var sequenceResult));
        Assert.Equal(
            PlaybackDecodedAudioSliceCollectorResult.SequenceRejected,
            sequenceResult);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Zero_or_negative_sample_count_is_rejected(int sampleFrames)
    {
        using var collector = new PlaybackDecodedAudioSliceCollector();
        Assert.False(collector.TryAppend(
            0, 1, PlaybackDecodedAudioSliceCollector.SampleFormat,
            48_000, 2, sampleFrames, ReadOnlyMemory<byte>.Empty,
            CancellationToken.None, out var result));
        Assert.Equal(PlaybackDecodedAudioSliceCollectorResult.InvalidFrame, result);
    }

    [Fact]
    public void Malformed_length_and_overflow_are_rejected()
    {
        using var malformed = new PlaybackDecodedAudioSliceCollector();
        Assert.False(malformed.TryAppend(
            0, 1, PlaybackDecodedAudioSliceCollector.SampleFormat,
            48_000, 2, 1_024, new byte[8], CancellationToken.None,
            out var malformedResult));
        Assert.Equal(
            PlaybackDecodedAudioSliceCollectorResult.InvalidFrame,
            malformedResult);

        using var overflow = new PlaybackDecodedAudioSliceCollector();
        Assert.False(overflow.TryAppend(
            0, 1, PlaybackDecodedAudioSliceCollector.SampleFormat,
            48_000, 2, int.MaxValue, ReadOnlyMemory<byte>.Empty,
            CancellationToken.None, out var overflowResult));
        Assert.Equal(
            PlaybackDecodedAudioSliceCollectorResult.InvalidFrame,
            overflowResult);
    }

    [Fact]
    public void Maximum_owned_bytes_and_cancellation_reject_without_publication()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlaybackDecodedAudioSliceCollector(new()
            {
                MaximumOwnedBytes =
                    PlaybackDecodedAudioSliceCollectorOptions.PayloadBytes - 1,
            }));

        using var collector = new PlaybackDecodedAudioSliceCollector();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.False(collector.TryAppend(
            0, 1, PlaybackDecodedAudioSliceCollector.SampleFormat,
            48_000, 2, 1_024, Payload(1, 1_024), cancellation.Token,
            out var result));
        Assert.Equal(PlaybackDecodedAudioSliceCollectorResult.Cancelled, result);
        Assert.False(collector.TryTakeCompletedBuffer(1, out _));
    }

    [Fact]
    public void Short_eos_equivalent_never_exposes_partial_payload()
    {
        using var collector = new PlaybackDecodedAudioSliceCollector();
        Assert.True(Append(collector, 0, Payload(1, 1_024), out _));
        Assert.True(Append(collector, 1, Payload(2, 1_024), out _));
        Assert.True(Append(collector, 2, Payload(3, 1_024), out _));

        Assert.False(collector.IsComplete);
        Assert.False(collector.TryTakeCompletedBuffer(1, out _));
        Assert.False(collector.Snapshot.PayloadPublished);
    }

    private static bool Append(
        PlaybackDecodedAudioSliceCollector collector,
        long sequence,
        byte[] payload,
        out PlaybackDecodedAudioSliceCollectorResult result) =>
        collector.TryAppend(
            sequence,
            generation: 1,
            PlaybackDecodedAudioSliceCollector.SampleFormat,
            PlaybackDecodedAudioSliceCollector.SampleRate,
            PlaybackDecodedAudioSliceCollector.Channels,
            sampleFrames: 1_024,
            payload,
            CancellationToken.None,
            out result);

    private static byte[] Payload(byte value, int sampleFrames) =>
        Enumerable.Repeat(
            value,
            sampleFrames * PlaybackDecodedAudioSliceCollector.BytesPerSampleFrame)
        .Select(item => (byte)item)
        .ToArray();

    private static byte[] PayloadWithExcludedTail()
    {
        var payload = Payload(4, 1_024);
        payload.AsSpan(768 * PlaybackDecodedAudioSliceCollector.BytesPerSampleFrame)
            .Fill(5);
        return payload;
    }
}
