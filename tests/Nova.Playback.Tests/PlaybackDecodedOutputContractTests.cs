using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecodedOutputContractTests
{
    private static readonly ChannelId ChannelId = new(
        Guid.ParseExact("531DC8DFC7FB42928AC2C0A626517A87", "N"));

    private static readonly ChannelSessionId SessionId = new(
        Guid.ParseExact("3F2504E04F8911D39A0C0305E82C3301", "N"));

    [Fact]
    public void AudioFrame_CreateAcceptsCanonicalInterleavedPcm()
    {
        var payload = new byte[16];

        var frame = PlaybackDecodedAudioFrame.Create(
            sequence: 7,
            timestamp: TimeSpan.FromMilliseconds(-10),
            duration: TimeSpan.FromMilliseconds(20),
            PlaybackDecodedFrameDiscontinuityKind.DecoderReset,
            PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved,
            sampleRate: 48_000,
            channelCount: 2,
            sampleCount: 2,
            payload);

        Assert.Equal(7, frame.Sequence);
        Assert.Equal(TimeSpan.FromMilliseconds(-10), frame.Timestamp);
        Assert.Equal(16, frame.Payload.Length);
        Assert.Equal(2, frame.ChannelCount);
        Assert.Equal(2, frame.SampleCount);
    }

    [Fact]
    public void AudioFrame_CreateRejectsMismatchedPayload()
    {
        Assert.Throws<ArgumentException>(() =>
            PlaybackDecodedAudioFrame.Create(
                sequence: 0,
                timestamp: TimeSpan.Zero,
                duration: TimeSpan.FromMilliseconds(20),
                PlaybackDecodedFrameDiscontinuityKind.None,
                PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                sampleRate: 48_000,
                channelCount: 2,
                sampleCount: 2,
                payload: new byte[7]));
    }

    [Fact]
    public void VideoFrame_CreateAcceptsCanonicalPackedBgra32()
    {
        var payload = new byte[16];

        var frame = PlaybackDecodedVideoFrame.Create(
            sequence: 3,
            timestamp: TimeSpan.FromMilliseconds(40),
            duration: TimeSpan.FromMilliseconds(40),
            PlaybackDecodedFrameDiscontinuityKind.None,
            PlaybackDecodedVideoPixelFormat.Bgra32,
            width: 2,
            height: 2,
            stride: 8,
            payload);

        Assert.Equal(2, frame.Width);
        Assert.Equal(2, frame.Height);
        Assert.Equal(8, frame.Stride);
        Assert.Equal(16, frame.Payload.Length);
    }

    [Fact]
    public void VideoFrame_CreateRejectsShortStride()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlaybackDecodedVideoFrame.Create(
                sequence: 0,
                timestamp: TimeSpan.Zero,
                duration: TimeSpan.FromMilliseconds(40),
                PlaybackDecodedFrameDiscontinuityKind.None,
                PlaybackDecodedVideoPixelFormat.Bgra32,
                width: 2,
                height: 2,
                stride: 7,
                payload: new byte[14]));
    }

    [Fact]
    public async Task Runner_DefaultsToContentDiscardingOutputSink()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        var factory = new CapturingDecoderFactory(_ => null);
        await using var runner = new PlaybackDecoderRunner(factory);

        await buffer.ConsumeAsync(
            CreatePacket(CreateFlvPayload()),
            CancellationToken.None);

        var result = await runner.RunAsync(buffer);

        Assert.Equal(
            PlaybackDecoderRunEndReason.DecoderUnsupported,
            result.EndReason);
        Assert.True(factory.Context.HasValue);
        Assert.Same(
            NullPlaybackDecodedFrameSink.Instance,
            factory.Context.Value.OutputSink);
    }

    [Fact]
    public async Task Runner_PassesInjectedOutputSinkToFactory()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        var sink = new BlockingSink();
        var factory = new CapturingDecoderFactory(_ => null);
        await using var runner = new PlaybackDecoderRunner(
            factory,
            outputSink: sink);

        await buffer.ConsumeAsync(
            CreatePacket(CreateFlvPayload()),
            CancellationToken.None);

        _ = await runner.RunAsync(buffer);

        Assert.True(factory.Context.HasValue);
        Assert.Same(sink, factory.Context.Value.OutputSink);
    }

    [Fact]
    public async Task AwaitedSinkAppliesBackpressureToDecoderRunner()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        var sink = new BlockingSink();
        var factory = new CapturingDecoderFactory(context =>
            new OutputWritingDecoder(context.OutputSink));
        await using var runner = new PlaybackDecoderRunner(
            factory,
            outputSink: sink);

        await buffer.ConsumeAsync(
            CreatePacket(CreateFlvPayload()),
            CancellationToken.None);
        buffer.Complete();

        var runTask = runner.RunAsync(buffer);
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(runTask.IsCompleted);
        Assert.Equal(1, buffer.Snapshot.OutstandingPacketCount);

        sink.Release.TrySetResult(true);
        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            PlaybackDecoderRunEndReason.SourceCompleted,
            result.EndReason);
        Assert.Equal(1, sink.AudioWriteCount);
        Assert.Equal(0, buffer.Snapshot.OutstandingPacketCount);
    }

    private static PcpChannelPacket CreatePacket(byte[] payload)
    {
        return new PcpChannelPacket(
            ChannelId,
            SessionId,
            sequence: 0,
            type: 1,
            wirePosition: 100,
            streamPosition: 100,
            payload,
            discontinuity: null);
    }

    private static byte[] CreateFlvPayload()
    {
        return
        [
            (byte)'F',
            (byte)'L',
            (byte)'V',
            1,
            5,
            0,
            0,
            0,
            9,
        ];
    }

    private sealed class CapturingDecoderFactory(
        Func<PlaybackDecoderCreationContext, IPlaybackDecoder?> create) :
        IPlaybackDecoderFactory
    {
        private readonly Func<PlaybackDecoderCreationContext, IPlaybackDecoder?>
            _create = create;

        public PlaybackDecoderCreationContext? Context { get; private set; }

        public IPlaybackDecoder? Create(
            PlaybackDecoderCreationContext context)
        {
            Context = context;
            return _create(context);
        }
    }

    private sealed class BlockingSink : IPlaybackDecodedFrameSink
    {
        private int _audioWriteCount;

        public int AudioWriteCount => Volatile.Read(ref _audioWriteCount);

        public TaskCompletionSource<bool> Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask WriteAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _audioWriteCount);
            Entered.TrySetResult(true);
            return new ValueTask(Release.Task.WaitAsync(cancellationToken));
        }

        public ValueTask WriteVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OutputWritingDecoder(
        IPlaybackDecodedFrameSink outputSink) : IPlaybackDecoder
    {
        private readonly IPlaybackDecodedFrameSink _outputSink = outputSink;
        private int _decodeCount;

        public async ValueTask DecodeAsync(
            PlaybackEncodedInput input,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _decodeCount) != 1)
            {
                return;
            }

            var frame = PlaybackDecodedAudioFrame.Create(
                sequence: 0,
                timestamp: TimeSpan.Zero,
                duration: TimeSpan.FromMilliseconds(20),
                PlaybackDecodedFrameDiscontinuityKind.None,
                PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                sampleRate: 48_000,
                channelCount: 2,
                sampleCount: 2,
                payload: new byte[8]);

            await _outputSink.WriteAudioAsync(
                frame,
                cancellationToken).ConfigureAwait(false);
        }

        public ValueTask ResetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
