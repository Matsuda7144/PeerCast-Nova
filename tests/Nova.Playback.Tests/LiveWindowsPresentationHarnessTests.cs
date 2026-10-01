using PeerCast.Nova.MediaEncoding;

namespace PeerCast.Nova.Playback.Tests;

public sealed class LiveWindowsPresentationHarnessTests
{
    private static readonly string[] NormalLifecycle = ["decoder-complete", "output-complete", "captured",
        "decoder-dispose", "output-dispose", "session-dispose"];
    [Fact]
    public async Task Decoded_forwarding_awaits_borrowed_video_and_audio_and_preserves_generation()
    {
        var terminal = new FakeOutput([]);
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        terminal.VideoWait = blocked.Task;
        var measured = new LiveDecodedOutput(terminal);
        Assert.Same(measured, measured.Create(0));
        var video = Video(0);
        var write = measured.WriteVideoAsync(video, default).AsTask();
        Assert.False(write.IsCompleted);
        Assert.Equal(video.Payload.ToArray(), Assert.Single(terminal.Video).Payload.ToArray());
        blocked.SetResult();
        await write;
        var audio = Audio(0);
        await measured.WriteAudioAsync(audio, default);
        Assert.Equal(audio.Payload.ToArray(), Assert.Single(terminal.Audio).Payload.ToArray());
        Assert.Equal(new long[] { 0 }, terminal.Generations);
        await measured.CompleteAsync(default);
        Assert.Equal(1, measured.Completions); Assert.Equal(1, terminal.Completions);
        await measured.DisposeAsync(); await measured.DisposeAsync();
        Assert.Equal(1, terminal.Disposals);
    }

    [Fact]
    public async Task Live_wire_is_the_only_route_to_terminal_and_QUIT_drains_before_teardown()
    {
        using var guard = new GuardDirectory();
        var session = new FakeSession();
        await Run(guard.Identity, session);
        Assert.Equal(96, session.Terminal.Video.Count);
        Assert.Equal(151, session.Terminal.Audio.Count);
        Assert.Equal(new long[] { 0 }, session.Terminal.Generations);
        Assert.Equal(1, session.Starts); Assert.Equal(1, session.Disposals);
        Assert.Equal(1, session.Terminal.Disposals);
        Assert.Equal(NormalLifecycle, session.Events);
        Assert.True(File.Exists(guard.Identity.GuardPath));
        using var marker = System.Text.Json.JsonDocument.Parse(File.ReadAllText(guard.Identity.GuardPath));
        Assert.Equal(guard.Identity.TaskId, marker.RootElement.GetProperty("TaskId").GetString());
        Assert.Equal(guard.Identity.RunId, marker.RootElement.GetProperty("RunId").GetString());
        Assert.Equal(guard.Identity.Head, marker.RootElement.GetProperty("sourceHead").GetString());
        Assert.Equal(1, marker.RootElement.GetProperty("presentationInitializationAttemptCount").GetInt32());
    }

    [Theory]
    [InlineData("presentation")]
    [InlineData("decoder")]
    [InlineData("completion")]
    public async Task Failures_propagate_without_successful_EOS_and_dispose_once(string failure)
    {
        using var guard = new GuardDirectory();
        var session = new FakeSession();
        if (failure == "presentation") { session.Terminal.WriteFailure = new InvalidOperationException("output failed"); }
        if (failure == "completion") { session.Terminal.CompletionFailure = new InvalidOperationException("drain failed"); }
        await Assert.ThrowsAnyAsync<Exception>(() => Run(guard.Identity, session,
            decoderFailure: failure == "decoder"));
        Assert.DoesNotContain("captured", session.Events);
        Assert.Equal(0, session.Terminal.Completions);
        Assert.Equal(1, session.Starts); Assert.Equal(1, session.Disposals);
        Assert.Equal(1, session.Terminal.Disposals);
        Assert.True(File.Exists(guard.Identity.GuardPath));
    }

    [Fact]
    public async Task Cancellation_aborts_without_QUIT_or_output_completion()
    {
        using var guard = new GuardDirectory();
        using var stop = new CancellationTokenSource();
        var session = new FakeSession();
        session.Terminal.OnVideo = stop.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(guard.Identity, session,
            cancellationToken: stop.Token));
        Assert.DoesNotContain("decoder-complete", session.Events);
        Assert.DoesNotContain("output-complete", session.Events);
        Assert.DoesNotContain("captured", session.Events);
        Assert.Equal(1, session.Disposals); Assert.Equal(1, session.Terminal.Disposals);
    }

    [Fact]
    public async Task Precancelled_start_does_not_reserve_or_construct_session()
    {
        using var guard = new GuardDirectory();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var creations = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LiveWindowsPresentationHarness.RunOnceAsync(guard.Identity, _ =>
            {
                creations++; return new FakeSession();
            }, (_, _, _) => Task.CompletedTask, stop.Token));
        Assert.Equal(0, creations);
        Assert.False(File.Exists(guard.Identity.GuardPath));
    }

    [Fact]
    public async Task Durable_guard_rejects_second_launch_even_with_a_different_Run_ID()
    {
        using var guard = new GuardDirectory();
        var session = new FakeSession();
        session.StartFailure = new InvalidOperationException("initialize failed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(guard.Identity, session));
        Assert.Equal(1, session.Disposals);
        var creations = 0;
        var anotherRun = guard.Identity with { RunId = "HARNESS-DRY-SECOND-RUN" };
        await Assert.ThrowsAsync<IOException>(() => LiveWindowsPresentationHarness.RunOnceAsync(anotherRun, _ =>
        {
            creations++; return new FakeSession();
        }, (_, _, _) => Task.CompletedTask, default));
        Assert.Equal(0, creations);
        Assert.Equal(1, session.Starts);
    }

    [Fact]
    public async Task Returning_without_downstream_completion_is_rejected()
    {
        using var guard = new GuardDirectory();
        var session = new FakeSession();
        await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(() =>
            LiveWindowsPresentationHarness.RunOnceAsync(guard.Identity, _ => session,
                (_, _, _) => Task.CompletedTask, default));
        Assert.Equal(1, session.Disposals);
        Assert.DoesNotContain("captured", session.Events);
    }

    [Fact]
    public async Task Teardown_failure_preserves_the_first_failure_without_retry()
    {
        using var guard = new GuardDirectory();
        var first = new InvalidOperationException("startup failed");
        var cleanup = new InvalidOperationException("cleanup failed");
        var session = new FakeSession { StartFailure = first, DisposalFailure = cleanup };
        var error = await Assert.ThrowsAsync<AggregateException>(() => Run(guard.Identity, session));
        Assert.Contains(first, error.InnerExceptions); Assert.Contains(cleanup, error.InnerExceptions);
        Assert.Equal(1, session.Starts); Assert.Equal(1, session.Disposals);
        Assert.True(File.Exists(guard.Identity.GuardPath));
    }

    [Fact]
    public void Dry_identity_reaches_window_configuration_without_initialization_or_guard()
    {
        using var guard = new GuardDirectory();
        guard.Identity.Validate();
        Assert.Contains(guard.Identity.RunId, guard.Identity.WindowTitle, StringComparison.Ordinal);
        Assert.Equal(guard.Identity.WindowTitle, guard.Identity.RunnerOptions.Title);
        Assert.Equal(640, guard.Identity.RunnerOptions.ClientWidth);
        Assert.Equal(360, guard.Identity.RunnerOptions.ClientHeight);
        Assert.False(File.Exists(guard.Identity.GuardPath));
        Assert.Throws<ArgumentException>(() => (guard.Identity with { TaskId = "../unsafe" }).Validate());
        Assert.Throws<ArgumentException>(() => (guard.Identity with { Head = "invalid" }).Validate());
    }

    private static Task Run(LivePresentationIdentity identity, FakeSession session,
        bool decoderFailure = false, CancellationToken cancellationToken = default) =>
        LiveWindowsPresentationHarness.RunOnceAsync(identity, _ => session, async (terminal, completed, token) =>
        {
            var encoder = new CapturingLiveEncoder(new FakeLiveEncoder());
            var factory = new LiveIdentityDecoderFactory(encoder,
                new EmittingDecoderFactory(session.Events, decoderFailure));
            var measured = new LiveDecodedOutput(terminal);
            await using var wire = new LivePcpWireLoopback(factory, measured);
            var source = new PacedSyntheticMediaSource(96, new ImmediateLiveClock());
            // No terminal output before PCP HEAD/DATA reception and decoder callbacks.
            Assert.Empty(session.Terminal.Video); Assert.Empty(session.Terminal.Audio);
            var publisher = new LiveEncodingPublisher(source, wire.Channel, wire.Session, wire);
            var result = await publisher.RunCoreAsync(() => encoder, token);
            token.ThrowIfCancellationRequested();
            Assert.True(result.Completed); Assert.False(result.Cancelled);
            Assert.Equal(96, result.Source.VideoFrames); Assert.Equal(150, result.Source.AudioBlocks);
            Assert.Equal(4, result.Fragments);
            await wire.CompleteAsync(token);
            Assert.Equal(encoder.Expected.Count, factory.Received.Count);
            Assert.Equal(247, factory.Received.Count);
            Assert.Equal(96, measured.VideoFrames); Assert.Equal(151, measured.AudioFrames);
            completed();
            await wire.DisposeAsync();
            Assert.True(factory.Disposed); Assert.True(measured.Disposed);
        }, cancellationToken);

    private static PlaybackDecodedVideoFrame Video(long sequence) => PlaybackDecodedVideoFrame.Create(
        sequence, TimeSpan.FromMilliseconds(sequence), TimeSpan.FromMilliseconds(1),
        PlaybackDecodedFrameDiscontinuityKind.None, PlaybackDecodedVideoPixelFormat.Bgra32,
        64, 48, 256, new byte[64 * 48 * 4]);
    private static PlaybackDecodedAudioFrame Audio(long sequence) => PlaybackDecodedAudioFrame.Create(
        sequence, TimeSpan.FromMilliseconds(sequence), TimeSpan.FromMilliseconds(1),
        PlaybackDecodedFrameDiscontinuityKind.None, PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved,
        48_000, 2, 1, new byte[8]);

    private sealed class EmittingDecoderFactory(List<string> events, bool fail) : IPlaybackDecoderFactory
    {
        public IPlaybackDecoder Create(PlaybackDecoderCreationContext context)
        {
            Assert.Equal(PlaybackMediaContainer.FragmentedMp4, context.Container);
            Assert.NotNull(context.EncodedStreamDescription);
            return new Decoder(context.OutputSink, events, fail);
        }
        private sealed class Decoder(IPlaybackDecodedFrameSink sink, List<string> events, bool fail) : IPlaybackDecoder
        {
            public async ValueTask DecodeAsync(PlaybackEncodedInput input, CancellationToken cancellationToken)
            {
                if (fail) { throw new InvalidOperationException("decoder failed"); }
                var metadata = Assert.IsType<PlaybackFmp4SampleMetadata>(input.Fmp4SampleMetadata);
                if (metadata.MediaRole == PlaybackEncodedMediaRole.Video)
                { await sink.WriteVideoAsync(Video(input.Sequence), cancellationToken); }
                else { await sink.WriteAudioAsync(Audio(input.Sequence), cancellationToken); }
            }
            public ValueTask CompleteAsync(CancellationToken cancellationToken)
            { events.Add("decoder-complete"); return ValueTask.CompletedTask; }
            public ValueTask ResetAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected reset");
            public ValueTask DisposeAsync() { events.Add("decoder-dispose"); return ValueTask.CompletedTask; }
        }
    }

    private sealed class FakeSession : ILiveWindowsPresentationSession
    {
        public List<string> Events { get; } = [];
        public FakeOutput Terminal { get; }
        public int Starts { get; private set; }
        public int Disposals { get; private set; }
        public Exception? StartFailure { get; set; }
        public Exception? DisposalFailure { get; set; }
        public FakeSession() => Terminal = new(Events);
        public IPlaybackPcpFmp4PresentationOutputGenerationFactory Output => Terminal;
        public ValueTask StartAsync(CancellationToken cancellationToken)
        {
            Starts++;
            if (StartFailure is not null) { throw StartFailure; }
            return ValueTask.CompletedTask;
        }
        public void CaptureCompleted() { Assert.Equal(1, Terminal.Completions); Events.Add("captured"); }
        public ValueTask DisposeAsync()
        {
            Disposals++; Events.Add("session-dispose");
            if (DisposalFailure is not null) { throw DisposalFailure; }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeOutput(List<string> events) : IPlaybackPcpFmp4PresentationOutputGenerationFactory,
        IPlaybackPcpFmp4PresentationOutputGeneration
    {
        public List<long> Generations { get; } = [];
        public List<PlaybackDecodedVideoFrame> Video { get; } = [];
        public List<PlaybackDecodedAudioFrame> Audio { get; } = [];
        public int Completions { get; private set; }
        public int Disposals { get; private set; }
        public Task? VideoWait { get; set; }
        public Action? OnVideo { get; set; }
        public Exception? WriteFailure { get; set; }
        public Exception? CompletionFailure { get; set; }
        public IPlaybackPcpFmp4PresentationOutputGeneration Create(long generation)
        { Generations.Add(generation); return this; }
        public async ValueTask WriteVideoAsync(PlaybackDecodedVideoFrame frame, CancellationToken cancellationToken)
        {
            if (WriteFailure is not null) { throw WriteFailure; }
            Video.Add(PlaybackDecodedVideoFrame.Create(frame.Sequence, frame.Timestamp, frame.Duration,
                frame.Discontinuity, frame.PixelFormat, frame.Width, frame.Height, frame.Stride, frame.Payload.ToArray()));
            OnVideo?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (VideoWait is not null) { await VideoWait.WaitAsync(cancellationToken); }
        }
        public ValueTask WriteAudioAsync(PlaybackDecodedAudioFrame frame, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (WriteFailure is not null) { throw WriteFailure; }
            Audio.Add(PlaybackDecodedAudioFrame.Create(frame.Sequence, frame.Timestamp, frame.Duration,
                frame.Discontinuity, frame.SampleFormat, frame.SampleRate, frame.ChannelCount, frame.SampleCount,
                frame.Payload.ToArray()));
            return ValueTask.CompletedTask;
        }
        public ValueTask CompleteAsync(CancellationToken cancellationToken)
        {
            if (CompletionFailure is not null) { throw CompletionFailure; }
            cancellationToken.ThrowIfCancellationRequested();
            Completions++; events.Add("output-complete"); return ValueTask.CompletedTask;
        }
        public ValueTask DisposeAsync() { Disposals++; events.Add("output-dispose"); return ValueTask.CompletedTask; }
    }

    private sealed class GuardDirectory : IDisposable
    {
        public LivePresentationIdentity Identity { get; }
        public GuardDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "Nova-HARNESS-DRY-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            Identity = new("HARNESS-DRY-TASK", "HARNESS-DRY-RUN", new string('a', 40), path);
        }
        public void Dispose()
        {
            if (File.Exists(Identity.GuardPath)) { File.Delete(Identity.GuardPath); }
            Directory.Delete(Identity.GuardDirectory);
        }
    }
}
