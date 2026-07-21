using static PeerCast.Nova.Playback.Tests.PlaybackPcpFlvAwaitedDecodeCompositionTestSupport;
using static PeerCast.Nova.Playback.Tests.PlaybackSourceBoundOneFrameDecodeTestSupport;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackPcpFlvAwaitedDecodeCompositionBoundaryTests
{
    [Fact]
    public async Task Consume_bootstrap_and_interleaved_packets_use_one_decode_generation()
    {
        var factory = new FakeGenerationFactory();
        await using var boundary = CreateBoundary(factory);
        var payload = ConcatForTest(
            BuildBootstrap(),
            BuildVideoNaluTag(),
            BuildAudioRawTag());

        await boundary.ConsumeAsync(CreatePacket(0, payload), CancellationToken.None);

        var generation = Assert.Single(factory.Generations);
        Assert.Equal(2, generation.PacketOperations.SubmitCount);
        Assert.Equal(0, generation.ReceiveOperations.ReceiveCount);
        Assert.Equal(0, generation.Settlement.CallCount);
        Assert.Equal(1, boundary.Snapshot.AcceptedPacketCount);
        Assert.Equal(payload.Length, boundary.Snapshot.AcceptedPayloadByteCount);
        Assert.True(boundary.Snapshot.HasDecoderGeneration);
        Assert.False(boundary.Snapshot.HasActiveSubmission);
        Assert.False(boundary.Snapshot.HasActiveDecodedCompletion);
    }

    [Fact]
    public async Task Consume_retained_packet_awaits_settlement_before_retry_and_callback_return()
    {
        var factory = new FakeGenerationFactory();
        await using var boundary = CreateBoundary(factory);
        await boundary.ConsumeAsync(
            CreatePacket(0, BuildBootstrap()),
            CancellationToken.None);
        var generation = Assert.Single(factory.Generations);
        generation.PacketOperations.SubmitResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        generation.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video)));
        generation.PacketOperations.RetryResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.Submitted);
        generation.Settlement.Pending = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var consume = boundary.ConsumeAsync(
            CreatePacket(1, BuildVideoNaluTag()),
            CancellationToken.None).AsTask();
        await generation.Settlement.Entered.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(consume.IsCompleted);
        Assert.Equal(0, generation.PacketOperations.RetryCount);
        Assert.True(boundary.Snapshot.HasActiveSubmission);
        Assert.True(boundary.Snapshot.HasActiveDecodedCompletion);

        generation.Settlement.Pending.SetResult(
            PlaybackPcpFlvDecodedSettlementResult.Consumed);
        await consume;

        Assert.Equal(1, generation.PacketOperations.RetryCount);
        Assert.Equal(1, boundary.Snapshot.SettledFrameCount);
        Assert.False(boundary.Snapshot.HasActiveSubmission);
        Assert.False(boundary.Snapshot.HasActiveDecodedCompletion);
        Assert.False(boundary.Snapshot.HasReliefRetry);
        Assert.Equal(2, boundary.Snapshot.AcceptedPacketCount);
    }

    [Fact]
    public async Task Consume_repeated_eagain_requires_one_frame_and_one_retry_per_cycle()
    {
        var factory = new FakeGenerationFactory();
        await using var boundary = CreateBoundary(factory);
        await boundary.ConsumeAsync(
            CreatePacket(0, BuildBootstrap()),
            CancellationToken.None);
        var generation = Assert.Single(factory.Generations);
        generation.PacketOperations.SubmitResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        generation.PacketOperations.RetryResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        generation.PacketOperations.RetryResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.Submitted);
        generation.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video, value: 0x41)));
        generation.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video, value: 0x42)));

        await boundary.ConsumeAsync(
            CreatePacket(1, BuildVideoNaluTag()),
            CancellationToken.None);

        Assert.Equal(2, generation.ReceiveOperations.ReceiveCount);
        Assert.Equal(2, generation.PacketOperations.RetryCount);
        Assert.Equal(2, generation.Settlement.CallCount);
        Assert.Equal(2, boundary.Snapshot.SettledFrameCount);
        Assert.Equal(4, boundary.Snapshot.DecodeOperationCount);
        Assert.False(boundary.Snapshot.HasActiveSubmission);
    }

    [Fact]
    public async Task Consume_settlement_rejection_faults_without_retrying_retained_packet()
    {
        var factory = new FakeGenerationFactory();
        await using var boundary = CreateBoundary(factory);
        await boundary.ConsumeAsync(
            CreatePacket(0, BuildBootstrap()),
            CancellationToken.None);
        var generation = Assert.Single(factory.Generations);
        generation.PacketOperations.SubmitResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        generation.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video)));
        generation.Settlement.Results.Enqueue(
            PlaybackPcpFlvDecodedSettlementResult.Rejected);

        var exception = await Assert.ThrowsAsync<PlaybackPcpFlvAwaitedDecodeException>(
            () => boundary.ConsumeAsync(
                CreatePacket(1, BuildVideoNaluTag()),
                CancellationToken.None).AsTask());

        Assert.Equal(PlaybackPcpFlvAwaitedDecodeResult.SettlementRejected, exception.Result);
        Assert.Equal(0, generation.PacketOperations.RetryCount);
        Assert.True(generation.Generation.IsDisposed);
        Assert.Equal(PlaybackPcpFlvAwaitedDecodeState.Faulted, boundary.Snapshot.State);
        Assert.Equal(1, boundary.Snapshot.AcceptedPacketCount);
    }

    [Fact]
    public async Task Consume_repeated_relief_obeys_shared_frame_budget()
    {
        var factory = new FakeGenerationFactory();
        var options = new PlaybackPcpFlvAwaitedDecodeOptions
        {
            MaximumDecodedFramesPerCall = 1,
        };
        await using var boundary = CreateBoundary(factory, options: options);
        await boundary.ConsumeAsync(
            CreatePacket(0, BuildBootstrap()),
            CancellationToken.None);
        var generation = Assert.Single(factory.Generations);
        generation.PacketOperations.SubmitResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        generation.PacketOperations.RetryResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        generation.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video, value: 0x31)));
        generation.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video, value: 0x32)));

        var exception = await Assert.ThrowsAsync<PlaybackPcpFlvAwaitedDecodeException>(
            () => boundary.ConsumeAsync(
                CreatePacket(1, BuildVideoNaluTag()),
                CancellationToken.None).AsTask());

        Assert.Equal(PlaybackPcpFlvAwaitedDecodeResult.WorkLimitExceeded, exception.Result);
        Assert.Equal(1, generation.Settlement.CallCount);
        Assert.Equal(1, generation.PacketOperations.RetryCount);
        Assert.Equal(1, boundary.Snapshot.AcceptedPacketCount);
    }

    [Fact]
    public async Task Ordinary_receive_is_one_frame_outside_pcp_callback()
    {
        var factory = new FakeGenerationFactory();
        await using var boundary = CreateBoundary(factory);
        await boundary.ConsumeAsync(
            CreatePacket(0, ConcatForTest(BuildBootstrap(), BuildVideoNaluTag())),
            CancellationToken.None);
        var generation = Assert.Single(factory.Generations);
        generation.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video)));

        var result = await boundary.ReceiveDecodedAsync(
            PlaybackBoundedDemuxMediaKind.Video);

        Assert.Equal(PlaybackPcpFlvAwaitedDecodeOperationResult.FrameConsumed, result);
        var completion = Assert.Single(generation.Settlement.Completions);
        Assert.Same(generation.Source, completion.Source);
        Assert.Equal(generation.Generation.DecoderGeneration, completion.DecoderGeneration);
        Assert.Equal(PlaybackBoundedDemuxMediaKind.Video, completion.MediaKind);
        Assert.Equal(0, completion.StreamId);
        Assert.Equal(0, completion.Sequence);
        Assert.Equal(4, completion.ManagedByteCount);
        Assert.True(completion.Frame.IsDisposed);
        Assert.Equal(1, generation.ReceiveOperations.ReceiveCount);
    }

    [Fact]
    public async Task Ordinary_need_more_input_is_a_normal_finite_result()
    {
        var factory = new FakeGenerationFactory();
        await using var boundary = CreateBoundary(factory);
        await boundary.ConsumeAsync(
            CreatePacket(0, BuildBootstrap()),
            CancellationToken.None);

        var result = await boundary.ReceiveDecodedAsync(
            PlaybackBoundedDemuxMediaKind.Audio);

        Assert.Equal(PlaybackPcpFlvAwaitedDecodeOperationResult.NeedMoreInput, result);
        Assert.Equal(PlaybackPcpFlvAwaitedDecodeState.Accepting, boundary.Snapshot.State);
        Assert.Equal(PlaybackPcpFlvAwaitedDecodeResult.NeedMoreInput, boundary.Snapshot.Result);
        Assert.Equal(0, boundary.Snapshot.SettledFrameCount);
    }

    [Fact]
    public async Task Finalization_accepts_flushes_then_drains_streams_independently()
    {
        var factory = new FakeGenerationFactory();
        await using var boundary = CreateBoundary(factory);
        await boundary.ConsumeAsync(
            CreatePacket(0, BuildBootstrap()),
            CancellationToken.None);
        var generation = Assert.Single(factory.Generations);

        await boundary.CompleteInputAsync();

        Assert.Equal(
            PlaybackPcpFlvAwaitedDecodeState.EncodedInputFinalized,
            boundary.Snapshot.State);
        Assert.True(boundary.Snapshot.VideoFlushAccepted);
        Assert.True(boundary.Snapshot.AudioFlushAccepted);
        Assert.Equal(
            PlaybackPcpFlvAwaitedDecodeOperationResult.StreamRequiresDrain,
            await boundary.ReceiveDecodedAsync(PlaybackBoundedDemuxMediaKind.Video));

        generation.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video)));
        Assert.Equal(
            PlaybackPcpFlvAwaitedDecodeOperationResult.FrameConsumed,
            await boundary.DrainDecodedAsync(PlaybackBoundedDemuxMediaKind.Video));

        generation.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.EndOfStream,
            null));
        Assert.Equal(
            PlaybackPcpFlvAwaitedDecodeOperationResult.EndOfStream,
            await boundary.DrainDecodedAsync(PlaybackBoundedDemuxMediaKind.Video));
        Assert.True(boundary.Snapshot.VideoEndOfStream);
        Assert.False(boundary.Snapshot.AudioEndOfStream);
        Assert.Equal(
            PlaybackPcpFlvAwaitedDecodeState.EncodedInputFinalized,
            boundary.Snapshot.State);

        generation.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.EndOfStream,
            null));
        Assert.Equal(
            PlaybackPcpFlvAwaitedDecodeOperationResult.EndOfStream,
            await boundary.DrainDecodedAsync(PlaybackBoundedDemuxMediaKind.Audio));
        Assert.True(boundary.Snapshot.AudioEndOfStream);
        Assert.Equal(
            PlaybackPcpFlvAwaitedDecodeState.DecodersDrained,
            boundary.Snapshot.State);
    }

    [Fact]
    public async Task Cancellation_during_settlement_prevents_retry_and_faults_generation()
    {
        var factory = new FakeGenerationFactory();
        await using var boundary = CreateBoundary(factory);
        await boundary.ConsumeAsync(
            CreatePacket(0, BuildBootstrap()),
            CancellationToken.None);
        var generation = Assert.Single(factory.Generations);
        generation.PacketOperations.SubmitResults.Enqueue(
            PlaybackMetadataAwareNativePacketResult.NeedFrameDrain);
        generation.ReceiveOperations.Results.Enqueue(new(
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied,
            CreateFrame(PlaybackBoundedDemuxMediaKind.Video)));
        generation.Settlement.Pending = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        var consume = boundary.ConsumeAsync(
            CreatePacket(1, BuildVideoNaluTag()),
            cancellation.Token).AsTask();
        await generation.Settlement.Entered.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consume);
        Assert.Equal(0, generation.PacketOperations.RetryCount);
        Assert.True(generation.Generation.IsDisposed);
        Assert.Equal(PlaybackPcpFlvAwaitedDecodeResult.Cancelled, boundary.Snapshot.Result);
    }

    [Fact]
    public async Task Discontinuity_disposes_settlement_before_old_native_generation()
    {
        var factory = new FakeGenerationFactory();
        await using var boundary = CreateBoundary(factory);
        await boundary.ConsumeAsync(
            CreatePacket(0, BuildBootstrap()),
            CancellationToken.None);
        var first = Assert.Single(factory.Generations);

        await boundary.ConsumeAsync(
            CreatePacket(1, BuildBootstrap(), discontinuity: true),
            CancellationToken.None);

        Assert.Equal(2, factory.Generations.Count);
        Assert.True(first.Settlement.IsDisposed);
        Assert.True(first.Generation.IsDisposed);
        var settlementIndex = factory.Log.IndexOf("settlement");
        var audioIndex = factory.Log.IndexOf("audio");
        var videoIndex = factory.Log.IndexOf("video");
        Assert.True(settlementIndex >= 0);
        Assert.True(audioIndex > settlementIndex);
        Assert.True(videoIndex > audioIndex);
        Assert.NotSame(first.Source, factory.Generations[1].Source);
        Assert.Equal(1, factory.Generations[1].Generation.DecoderGeneration);
    }

    [Fact]
    public async Task Invalid_identity_fails_closed_with_fixed_message()
    {
        var factory = new FakeGenerationFactory();
        await using var boundary = CreateBoundary(factory);
        var packet = new PeerCast.Nova.Network.Pcp.PcpChannelPacket(
            new PeerCast.Nova.Core.Channels.ChannelId(Guid.NewGuid()),
            SessionId,
            0,
            1,
            100,
            100,
            BuildBootstrap(),
            discontinuity: null);

        var exception = await Assert.ThrowsAsync<PlaybackPcpFlvAwaitedDecodeException>(
            () => boundary.ConsumeAsync(packet, CancellationToken.None).AsTask());

        Assert.Equal(PlaybackPcpFlvAwaitedDecodeResult.InvalidIdentity, exception.Result);
        Assert.Equal(
            "The awaited PCP-to-FLV decode composition boundary failed.",
            exception.Message);
        Assert.True(factory.IsDisposed);
    }

    private static byte[] ConcatForTest(params byte[][] values)
    {
        var length = values.Sum(value => value.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var value in values)
        {
            value.CopyTo(result.AsSpan(offset));
            offset += value.Length;
        }

        return result;
    }
}
