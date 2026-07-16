using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderRunnerTests
{
    private static readonly ChannelId ChannelId = new(
        Guid.ParseExact("531DC8DFC7FB42928AC2C0A626517A87", "N"));

    private static readonly ChannelSessionId SessionId = new(
        Guid.ParseExact("3F2504E04F8911D39A0C0305E82C3301", "N"));

    [Fact]
    public async Task SplitHeader_ReplaysBootstrapAndRecognizingRemainderOnce()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        var decoder = new RecordingDecoder();
        var factory = new RecordingDecoderFactory(_ => decoder);
        await using var runner = new PlaybackDecoderRunner(
            factory,
            new PlaybackDecoderRunnerOptions
            {
                MaxBootstrapBytes = 16,
            });
        var payload = CreateFlvPayload(24);

        await ConsumeAsync(buffer, 0, payload[..2]);
        await ConsumeAsync(buffer, 1, payload[2..]);
        buffer.Complete();

        var result = await runner.RunAsync(buffer);

        Assert.Equal(
            PlaybackDecoderRunEndReason.SourceCompleted,
            result.EndReason);
        Assert.Equal(PlaybackMediaContainer.FlashVideo, result.Container);
        Assert.Equal(2, result.SubmittedInputCount);
        Assert.Equal(payload.Length, result.SubmittedByteCount);
        Assert.Collection(
            decoder.Calls.Where(call =>
                call.Kind is RecordedDecoderCallKind.Decode).ToArray(),
            call =>
            {
                Assert.Equal(PlaybackEncodedInputKind.Bootstrap, call.InputKind);
                Assert.Equal(payload[..16], call.Payload);
            },
            call =>
            {
                Assert.Equal(PlaybackEncodedInputKind.Packet, call.InputKind);
                Assert.Equal(payload[16..], call.Payload);
                Assert.Equal(115L, call.StreamPosition);
            });
        Assert.Equal(payload, decoder.DecodedBytes.ToArray());
        Assert.Equal(0, buffer.Snapshot.OutstandingPacketCount);
    }

    [Fact]
    public async Task DiscontinuityBeforeRecognition_DiscardsPriorBootstrap()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        var decoder = new RecordingDecoder();
        await using var runner = new PlaybackDecoderRunner(
            new RecordingDecoderFactory(_ => decoder));
        var flv = CreateFlvPayload(12);

        await ConsumeAsync(buffer, 0, [0xde, 0xad, 0xbe, 0xef]);
        await buffer.ConsumeAsync(
            CreatePacket(1, flv, sourceDiscontinuity: true),
            CancellationToken.None);
        buffer.Complete();

        var result = await runner.RunAsync(buffer);

        Assert.Equal(
            PlaybackDecoderRunEndReason.SourceCompleted,
            result.EndReason);
        Assert.Equal(flv, decoder.DecodedBytes.ToArray());
        var decode = Assert.Single(
            decoder.Calls,
            call => call.Kind == RecordedDecoderCallKind.Decode);
        Assert.Equal(PlaybackEncodedInputKind.Bootstrap, decode.InputKind);
        Assert.Equal(PlaybackDiscontinuityKind.Source, decode.Discontinuity);
    }

    [Fact]
    public async Task DiscontinuityAfterRecognition_ResetsBeforePacketDecode()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        var decoder = new RecordingDecoder();
        await using var runner = new PlaybackDecoderRunner(
            new RecordingDecoderFactory(_ => decoder));

        await ConsumeAsync(buffer, 0, CreateFlvPayload(9));
        await buffer.ConsumeAsync(
            CreatePacket(1, [0x10, 0x20], sourceDiscontinuity: true),
            CancellationToken.None);
        buffer.Complete();

        var result = await runner.RunAsync(buffer);

        Assert.Equal(1, result.ResetCount);
        Assert.Equal(
            [
                RecordedDecoderCallKind.Decode,
                RecordedDecoderCallKind.Reset,
                RecordedDecoderCallKind.Decode,
                RecordedDecoderCallKind.Complete,
                RecordedDecoderCallKind.Dispose,
            ],
            decoder.Calls.Select(call => call.Kind).ToArray());
        Assert.Equal(
            PlaybackEncodedInputKind.Packet,
            decoder.Calls[2].InputKind);
        Assert.Equal(
            PlaybackDiscontinuityKind.Source,
            decoder.Calls[2].Discontinuity);
    }

    [Fact]
    public async Task UnsupportedContainer_ReturnsFixedResultWithoutDecoder()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        var factory = new RecordingDecoderFactory(_ => null);
        await using var runner = new PlaybackDecoderRunner(factory);

        await ConsumeAsync(buffer, 0, CreateFlvPayload(9));

        var result = await runner.RunAsync(buffer);

        Assert.Equal(
            PlaybackDecoderRunEndReason.DecoderUnsupported,
            result.EndReason);
        Assert.Equal(PlaybackMediaContainer.FlashVideo, result.Container);
        Assert.Equal(1, factory.CreateCount);
        Assert.Equal(0, result.SubmittedInputCount);
        Assert.Equal(0, buffer.Snapshot.OutstandingPacketCount);
    }

    [Fact]
    public async Task FactoryException_ReleasesCurrentLease()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        await using var runner = new PlaybackDecoderRunner(
            new RecordingDecoderFactory(_ =>
                throw new InvalidOperationException("factory failed")));

        await ConsumeAsync(buffer, 0, CreateFlvPayload(9));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(buffer));
        Assert.Equal(0, buffer.Snapshot.OutstandingPacketCount);
    }

    [Fact]
    public async Task DecoderException_ReleasesLeaseAndDisposesDecoder()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        var decoder = new RecordingDecoder
        {
            DecodeHandler = (_, _, _) => new ValueTask(
                Task.FromException(
                    new InvalidOperationException("decode failed"))),
        };
        await using var runner = new PlaybackDecoderRunner(
            new RecordingDecoderFactory(_ => decoder));

        await ConsumeAsync(buffer, 0, CreateFlvPayload(9));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(buffer));
        Assert.Equal(0, buffer.Snapshot.OutstandingPacketCount);
        Assert.Equal(1, decoder.DisposeCount);
    }

    [Fact]
    public async Task CallerCancellation_IgnoredByDecoderReturnsWithinDeadline()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        var callbackGate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var decoder = new RecordingDecoder
        {
            DecodeHandler = (index, _, _) =>
            {
                if (index == 1)
                {
                    callbackEntered.TrySetResult(true);
                    return new ValueTask(callbackGate.Task);
                }

                return ValueTask.CompletedTask;
            },
        };
        await using var runner = new PlaybackDecoderRunner(
            new RecordingDecoderFactory(_ => decoder),
            new PlaybackDecoderRunnerOptions
            {
                MaxBootstrapBytes = 16,
                StopTimeout = TimeSpan.FromMilliseconds(20),
            });
        using var callerCancellation = new CancellationTokenSource();

        await ConsumeAsync(buffer, 0, CreateFlvPayload(24));
        var runTask = runner.RunAsync(buffer, callerCancellation.Token);
        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        callerCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runTask.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(runner.DeferredCleanupPending);
        Assert.Equal(1, buffer.Snapshot.OutstandingPacketCount);

        callbackGate.TrySetResult(true);
        await decoder.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, buffer.Snapshot.OutstandingPacketCount);
        await WaitUntilAsync(() => !runner.DeferredCleanupPending);
    }

    [Fact]
    public async Task OwnerStop_IgnoredByDecoderReturnsFixedDeferredResult()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        var callbackGate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var decoder = new RecordingDecoder
        {
            DecodeHandler = (index, _, _) =>
            {
                if (index == 1)
                {
                    callbackEntered.TrySetResult(true);
                    return new ValueTask(callbackGate.Task);
                }

                return ValueTask.CompletedTask;
            },
        };
        await using var runner = new PlaybackDecoderRunner(
            new RecordingDecoderFactory(_ => decoder),
            new PlaybackDecoderRunnerOptions
            {
                MaxBootstrapBytes = 16,
                StopTimeout = TimeSpan.FromMilliseconds(20),
            });

        await ConsumeAsync(buffer, 0, CreateFlvPayload(24));
        var runTask = runner.RunAsync(buffer);
        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await runner.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        var result = await runTask;

        Assert.Equal(
            PlaybackDecoderRunEndReason.OwnerStopped,
            result.EndReason);
        Assert.True(result.CleanupDeferred);
        Assert.True(runner.DeferredCleanupPending);
        Assert.Equal(1, buffer.Snapshot.OutstandingPacketCount);

        callbackGate.TrySetResult(true);
        await decoder.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, buffer.Snapshot.OutstandingPacketCount);
        await WaitUntilAsync(() => !runner.DeferredCleanupPending);
    }

    [Fact]
    public async Task CompleteAndDispose_AreOrderedAfterLastInput()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        var decoder = new RecordingDecoder();
        await using var runner = new PlaybackDecoderRunner(
            new RecordingDecoderFactory(_ => decoder));

        await ConsumeAsync(buffer, 0, CreateFlvPayload(9));
        buffer.Complete();

        var result = await runner.RunAsync(buffer);

        Assert.Equal(
            PlaybackDecoderRunEndReason.SourceCompleted,
            result.EndReason);
        Assert.Equal(
            RecordedDecoderCallKind.Complete,
            decoder.Calls[^2].Kind);
        Assert.Equal(
            RecordedDecoderCallKind.Dispose,
            decoder.Calls[^1].Kind);
        Assert.Equal(1, decoder.DisposeCount);
    }

    [Fact]
    public async Task CompleteIgnoringCancellation_UsesFiniteDeadline()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        var completeGate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completeEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var decoder = new RecordingDecoder
        {
            CompleteHandler = _ =>
            {
                completeEntered.TrySetResult(true);
                return new ValueTask(completeGate.Task);
            },
        };
        await using var runner = new PlaybackDecoderRunner(
            new RecordingDecoderFactory(_ => decoder),
            new PlaybackDecoderRunnerOptions
            {
                StopTimeout = TimeSpan.FromMilliseconds(20),
            });

        await ConsumeAsync(buffer, 0, CreateFlvPayload(9));
        buffer.Complete();

        var runTask = runner.RunAsync(buffer);
        await completeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            PlaybackDecoderRunEndReason.SourceCompleted,
            result.EndReason);
        Assert.True(result.CleanupDeferred);
        Assert.True(runner.DeferredCleanupPending);

        completeGate.TrySetResult(true);
        await decoder.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !runner.DeferredCleanupPending);
    }

    [Fact]
    public async Task SecondRun_IsRejectedAfterFirstRunCompletes()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        await using var runner = new PlaybackDecoderRunner(
            new RecordingDecoderFactory(_ => null));
        buffer.Complete();

        _ = await runner.RunAsync(buffer);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(buffer));
    }

    [Fact]
    public async Task RunAfterDispose_IsRejected()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        var runner = new PlaybackDecoderRunner(
            new RecordingDecoderFactory(_ => null));

        await runner.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => runner.RunAsync(buffer));
    }

    [Fact]
    public void OptionsAndResult_UseFiniteContentFreeFields()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackDecoderRunnerOptions
            {
                MaxBootstrapBytes = 15,
            }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackDecoderRunnerOptions
            {
                MaxDiscontinuities = -1,
            }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackDecoderRunnerOptions
            {
                StopTimeout = TimeSpan.Zero,
            }.Validate());

        var allowedTypes = new HashSet<Type>
        {
            typeof(PlaybackDecoderRunEndReason),
            typeof(PlaybackMediaClassificationState),
            typeof(PlaybackMediaContainer),
            typeof(PlaybackMediaClassificationBasis?),
            typeof(long),
            typeof(bool),
        };
        var unexpected = typeof(PlaybackDecoderRunResult)
            .GetProperties()
            .Where(property => !allowedTypes.Contains(property.PropertyType))
            .Select(property => property.Name)
            .ToArray();

        Assert.Empty(unexpected);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(2));

        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static ValueTask ConsumeAsync(
        PcpPlaybackLatencyBuffer buffer,
        long sequence,
        byte[] payload)
    {
        return buffer.ConsumeAsync(
            CreatePacket(sequence, payload),
            CancellationToken.None);
    }

    private static PcpChannelPacket CreatePacket(
        long sequence,
        byte[] payload,
        bool sourceDiscontinuity = false)
    {
        return new PcpChannelPacket(
            ChannelId,
            SessionId,
            sequence,
            type: 1,
            wirePosition: checked((uint)(100 + sequence)),
            streamPosition: 100 + sequence,
            payload,
            sourceDiscontinuity
                ? new PcpStreamDiscontinuity(99, 50, 99)
                : null);
    }

    private static byte[] CreateFlvPayload(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 9);

        var payload = new byte[length];
        payload[0] = (byte)'F';
        payload[1] = (byte)'L';
        payload[2] = (byte)'V';
        payload[3] = 1;
        payload[4] = 5;
        payload[8] = 9;

        for (var index = 9; index < payload.Length; index++)
        {
            payload[index] = checked((byte)index);
        }

        return payload;
    }

    private enum RecordedDecoderCallKind
    {
        Decode,
        Reset,
        Complete,
        Dispose,
    }

    private sealed record RecordedDecoderCall(
        RecordedDecoderCallKind Kind,
        PlaybackEncodedInputKind? InputKind = null,
        byte[]? Payload = null,
        long? StreamPosition = null,
        PlaybackDiscontinuityKind Discontinuity =
            PlaybackDiscontinuityKind.None);

    private sealed class RecordingDecoderFactory(
        Func<PlaybackDecoderCreationContext, IPlaybackDecoder?> create) :
        IPlaybackDecoderFactory
    {
        private readonly Func<PlaybackDecoderCreationContext, IPlaybackDecoder?>
            _create = create;

        public int CreateCount { get; private set; }

        public IPlaybackDecoder? Create(
            PlaybackDecoderCreationContext context)
        {
            CreateCount++;
            return _create(context);
        }
    }

    private sealed class RecordingDecoder : IPlaybackDecoder
    {
        private readonly object _gate = new();
        private readonly List<RecordedDecoderCall> _calls = [];
        private readonly List<byte> _decodedBytes = [];
        private int _decodeCount;
        private int _disposeCount;

        public Func<int, PlaybackEncodedInput, CancellationToken, ValueTask>? DecodeHandler { get; init; }

        public Func<CancellationToken, ValueTask>? CompleteHandler
        { get; init; }

        public RecordedDecoderCall[] Calls
        {
            get
            {
                lock (_gate)
                {
                    return _calls.ToArray();
                }
            }
        }

        public IReadOnlyList<byte> DecodedBytes
        {
            get
            {
                lock (_gate)
                {
                    return _decodedBytes.ToArray();
                }
            }
        }

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public TaskCompletionSource<bool> Disposed { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask DecodeAsync(
            PlaybackEncodedInput input,
            CancellationToken cancellationToken)
        {
            int index;

            lock (_gate)
            {
                index = _decodeCount++;
                var payload = input.Payload.ToArray();
                _decodedBytes.AddRange(payload);
                _calls.Add(new RecordedDecoderCall(
                    RecordedDecoderCallKind.Decode,
                    input.Kind,
                    payload,
                    input.StreamPosition,
                    input.Discontinuity));
            }

            return DecodeHandler?.Invoke(
                index,
                input,
                cancellationToken) ?? ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                _calls.Add(new RecordedDecoderCall(
                    RecordedDecoderCallKind.Reset));
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                _calls.Add(new RecordedDecoderCall(
                    RecordedDecoderCallKind.Complete));
            }

            return CompleteHandler?.Invoke(cancellationToken) ??
                ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                _calls.Add(new RecordedDecoderCall(
                    RecordedDecoderCallKind.Dispose));
            }

            Interlocked.Increment(ref _disposeCount);
            Disposed.TrySetResult(true);
            return ValueTask.CompletedTask;
        }
    }
}
