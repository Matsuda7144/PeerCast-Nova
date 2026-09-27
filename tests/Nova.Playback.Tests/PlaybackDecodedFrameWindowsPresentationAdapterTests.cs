namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecodedFrameWindowsPresentationAdapterTests
{
    [Fact]
    public async Task ForwardsCanonicalVideoAndAudioFramesAndPreservesExactTimestamps()
    {
        var clock = new ManualClock();
        var output = new RecordingSink();
        var dispatcher = new InlineDispatcher();
        await using var adapter = CreateAdapter(clock, output, dispatcher);
        var videoTimestamp = TimeSpan.FromTicks(1_234_567);
        var audioTimestamp = TimeSpan.FromTicks(1_234_567);

        await adapter.WriteVideoAsync(Video(0, videoTimestamp), default);
        await adapter.WriteAudioAsync(Audio(1, audioTimestamp), default);

        Assert.Single(output.Video);
        Assert.Single(output.Audio);
        Assert.Equal(videoTimestamp, output.Video[0].Timestamp);
        Assert.Equal(audioTimestamp, output.Audio[0].Timestamp);
        Assert.Equal(new byte[16], output.Video[0].Payload);
        Assert.Equal(new byte[32], output.Audio[0].Payload);
        Assert.Equal(1, adapter.Snapshot.VideoPresentedCount);
        Assert.Equal(1, adapter.Snapshot.AudioWrittenCount);
        Assert.Equal(videoTimestamp, adapter.Snapshot.FirstVideoPresentationTimestamp);
        Assert.Equal(audioTimestamp, adapter.Snapshot.FirstAudioPresentationTimestamp);
        Assert.Equal(1, adapter.Snapshot.MaximumOutputInFlight);
    }

    [Fact]
    public async Task PacesEarlyFrameFromCheckedPresentationTimestampWithoutQueuing()
    {
        var clock = new ManualClock();
        var output = new RecordingSink();
        var delay = new AdvancingDelay(clock);
        await using var adapter = CreateAdapter(clock, output, new InlineDispatcher(), delay);

        await adapter.WriteVideoAsync(Video(0, TimeSpan.Zero), default);
        await adapter.WriteVideoAsync(Video(1, TimeSpan.FromMilliseconds(80)), default);

        Assert.Equal(TimeSpan.FromMilliseconds(80), clock.GetTimestamp());
        Assert.Equal([TimeSpan.FromMilliseconds(80)], delay.Delays);
        Assert.Equal(2, adapter.Snapshot.VideoPresentedCount);
        Assert.Equal(1, adapter.Snapshot.MaximumOutputInFlight);
    }

    [Fact]
    public async Task DecoderResetReconfiguresBothOutputsAndResetsClockBeforeForwardingFrame()
    {
        var clock = new ManualClock();
        var output = new RecordingSink();
        var resetCalls = new List<(long Video, long Audio)>();
        await using var adapter = CreateAdapter(
            clock,
            output,
            new InlineDispatcher(),
            resetOutput: (video, audio, _) =>
            {
                resetCalls.Add((video, audio));
                return ValueTask.FromResult(true);
            });

        await adapter.WriteVideoAsync(Video(0, TimeSpan.Zero), default);
        await adapter.WriteAudioAsync(
            Audio(1, TimeSpan.Zero, PlaybackDecodedFrameDiscontinuityKind.DecoderReset),
            default);
        await adapter.WriteVideoAsync(Video(2, TimeSpan.Zero), default);

        Assert.Equal([(1L, 1L)], resetCalls);
        Assert.Equal(1, adapter.Snapshot.ResetCount);
        Assert.Equal(2, adapter.Snapshot.VideoPresentedCount);
        Assert.Equal(1, adapter.Snapshot.AudioWrittenCount);
        Assert.Equal(
            PlaybackDecodedFrameDiscontinuityKind.DecoderReset,
            output.Audio[0].Discontinuity);
    }

    [Fact]
    public async Task CompletionDrainsAudioBeforeClosingFrameAdmission()
    {
        var clock = new ManualClock();
        var output = new RecordingSink();
        var reads = 0;
        await using var adapter = CreateAdapter(
            clock,
            output,
            new InlineDispatcher(),
            readAudioPadding: _ => ValueTask.FromResult(
                ++reads < 2
                    ? new PlaybackWindowsAudioPaddingRead(
                        PlaybackWindowsAudioPaddingReadResult.Available,
                        128)
                    : new PlaybackWindowsAudioPaddingRead(
                        PlaybackWindowsAudioPaddingReadResult.Available,
                        0)));

        await adapter.WriteAudioAsync(Audio(0, TimeSpan.Zero), default);
        var completion = await adapter.CompleteAsync();

        Assert.Equal(
            PlaybackDecodedFrameWindowsPresentationCompletionResult.Completed,
            completion);
        Assert.Equal(2, reads);
        Assert.Equal(0, adapter.Snapshot.AudioPaddingAtCompletion.PaddingFrameCount);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await adapter.WriteAudioAsync(Audio(1, TimeSpan.Zero), default));
    }

    [Fact]
    public async Task AwaitedOutputBackpressureKeepsOnlyOneFrameInFlight()
    {
        var clock = new ManualClock();
        var output = new BlockingSink();
        await using var adapter = CreateAdapter(clock, output, new InlineDispatcher());
        var payload = new byte[16];
        payload[0] = 0x5a;

        var first = adapter.WriteVideoAsync(
            Video(0, TimeSpan.Zero, payload: payload),
            default).AsTask();
        await output.FirstVideoEntered.Task;
        var second = adapter.WriteVideoAsync(Video(1, TimeSpan.Zero), default).AsTask();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await adapter.WriteVideoAsync(Video(2, TimeSpan.Zero), default));
        Assert.Equal(1, adapter.Snapshot.CurrentOutputInFlight);
        Assert.Equal(1, adapter.Snapshot.MaximumOutputInFlight);
        Assert.Equal(1, output.VideoCount);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        output.ReleaseFirstVideo.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(2, output.VideoCount);
        Assert.Equal(0x5a, output.FirstVideoPayloadStartByte);
        Assert.Equal(0, adapter.Snapshot.CurrentOutputInFlight);
        Assert.Equal(1, adapter.Snapshot.MaximumOutputInFlight);
    }

    [Fact]
    public async Task InvalidDefaultFrameCancellationAndDisposalFailClosed()
    {
        var clock = new ManualClock();
        var output = new RecordingSink();
        var adapter = CreateAdapter(clock, output, new InlineDispatcher());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await adapter.WriteVideoAsync(default, default));
        await using (var invalidAudioAdapter = CreateAdapter(
            clock,
            output,
            new InlineDispatcher()))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await invalidAudioAdapter.WriteAudioAsync(default, default));
        }

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await adapter.WriteAudioAsync(Audio(0, TimeSpan.Zero), cancelled.Token));
        Assert.Empty(output.Video);
        Assert.Empty(output.Audio);

        await adapter.DisposeAsync();
        Assert.Equal(
            PlaybackDecodedFrameWindowsPresentationState.Disposed,
            adapter.Snapshot.State);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await adapter.WriteVideoAsync(Video(0, TimeSpan.Zero), default));
    }

    private static PlaybackDecodedFrameWindowsPresentationAdapter CreateAdapter(
        ManualClock clock,
        IPlaybackDecodedFrameSink output,
        IPlaybackWindowsOutputDispatcher dispatcher,
        IPlaybackDecodedFrameWindowsPresentationDelay? delay = null,
        Func<long, long, CancellationToken, ValueTask<bool>>? resetOutput = null,
        Func<CancellationToken, ValueTask<PlaybackWindowsAudioPaddingRead>>?
            readAudioPadding = null)
    {
        var timing = new PlaybackAvTimingDecisionBoundary(
            clock,
            new PlaybackAvTimingTolerances(),
            videoGeneration: 0,
            audioGeneration: 0);
        var reset = resetOutput ?? ((_, _, _) => ValueTask.FromResult(true));
        var readPadding = readAudioPadding ?? (_ => ValueTask.FromResult(
            new PlaybackWindowsAudioPaddingRead(
                PlaybackWindowsAudioPaddingReadResult.Available,
                0)));
        var effectiveDelay = delay ?? new AdvancingDelay(clock);
        return new(
            dispatcher,
            output,
            timing,
            videoGeneration: 0,
            audioGeneration: 0,
            reset,
            readPadding,
            effectiveDelay);
    }

    private static PlaybackDecodedVideoFrame Video(
        long sequence,
        TimeSpan timestamp,
        PlaybackDecodedFrameDiscontinuityKind discontinuity =
            PlaybackDecodedFrameDiscontinuityKind.None,
        byte[]? payload = null) =>
        PlaybackDecodedVideoFrame.Create(
            sequence,
            timestamp,
            TimeSpan.FromMilliseconds(10),
            discontinuity,
            PlaybackDecodedVideoPixelFormat.Bgra32,
            2,
            2,
            8,
            payload ?? new byte[16]);

    private static PlaybackDecodedAudioFrame Audio(
        long sequence,
        TimeSpan timestamp,
        PlaybackDecodedFrameDiscontinuityKind discontinuity =
            PlaybackDecodedFrameDiscontinuityKind.None) =>
        PlaybackDecodedAudioFrame.Create(
            sequence,
            timestamp,
            TimeSpan.FromMilliseconds(10),
            discontinuity,
            PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved,
            48_000,
            2,
            4,
            new byte[32]);

    private sealed class ManualClock : IPlaybackMonotonicClock
    {
        public TimeSpan Current { get; private set; }

        public TimeSpan GetTimestamp() => Current;

        public void Advance(TimeSpan duration) => Current = checked(Current + duration);
    }

    private sealed class AdvancingDelay(ManualClock clock) :
        IPlaybackDecodedFrameWindowsPresentationDelay
    {
        public List<TimeSpan> Delays { get; } = [];

        public ValueTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(duration);
            clock.Advance(duration);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class InlineDispatcher : IPlaybackWindowsOutputDispatcher
    {
        public bool HasAccess => true;

        public async ValueTask<PlaybackWindowsOutputDispatcherInvocation<T>> InvokeAsync<T>(
            Func<CancellationToken, ValueTask<T>> operation,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return new(
                    PlaybackWindowsOutputDispatcherInvocationResult.Completed,
                    await operation(cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new(PlaybackWindowsOutputDispatcherInvocationResult.Cancelled, default!);
            }
            catch
            {
                return new(PlaybackWindowsOutputDispatcherInvocationResult.Faulted, default!);
            }
        }
    }

    private sealed class RecordingSink : IPlaybackDecodedFrameSink
    {
        public List<PlaybackDecodedVideoFrame> Video { get; } = [];

        public List<PlaybackDecodedAudioFrame> Audio { get; } = [];

        public ValueTask WriteAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Audio.Add(PlaybackDecodedAudioFrame.Create(
                frame.Sequence,
                frame.Timestamp,
                frame.Duration,
                frame.Discontinuity,
                frame.SampleFormat,
                frame.SampleRate,
                frame.ChannelCount,
                frame.SampleCount,
                frame.Payload.ToArray()));
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Video.Add(PlaybackDecodedVideoFrame.Create(
                frame.Sequence,
                frame.Timestamp,
                frame.Duration,
                frame.Discontinuity,
                frame.PixelFormat,
                frame.Width,
                frame.Height,
                frame.Stride,
                frame.Payload.ToArray()));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingSink : IPlaybackDecodedFrameSink
    {
        public TaskCompletionSource FirstVideoEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstVideo { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int VideoCount { get; private set; }

        public int? FirstVideoPayloadStartByte { get; private set; }

        public ValueTask WriteAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public async ValueTask WriteVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken)
        {
            VideoCount++;
            if (VideoCount == 1)
            {
                FirstVideoEntered.TrySetResult();
                await ReleaseFirstVideo.Task.WaitAsync(cancellationToken);
                FirstVideoPayloadStartByte = frame.Payload.Span[0];
            }
        }
    }
}
