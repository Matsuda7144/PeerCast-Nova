using Xunit.Abstractions;

namespace PeerCast.Nova.Playback.Tests;

public sealed class LiveWindowsPhysicalAcceptanceFactAttribute : FactAttribute
{
    private static readonly string[] RequiredIdentityFields = ["TASK_ID", "RUN_ID", "HEAD", "GUARD_DIRECTORY"];
    public LiveWindowsPhysicalAcceptanceFactAttribute()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000) || !Environment.Is64BitProcess ||
            Environment.GetEnvironmentVariable("NOVA_LIVE_PHYSICAL_RUN_ONCE") != "RUN-ONCE" ||
            RequiredIdentityFields.Any(name =>
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NOVA_LIVE_PHYSICAL_" + name))) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NOVA_FMP4_FFMPEG_TEST_RUNTIME")))
        {
            Skip = "Requires explicit live one-shot authorization, caller identities, exclusive guard directory and pinned runtime.";
        }
    }
}

public sealed class LiveWindowsPresentationAcceptanceTests(ITestOutputHelper output)
{
    [FfmpegNativeAcceptanceFact]
    public async Task Paced_native_live_decoder_forwards_to_fake_terminal_without_Windows_output()
    {
        var terminal = new LiveDecodedOutput();
        await new LiveEncodingNativeAcceptanceTests(output).RunAsync(true, terminal, completed: () =>
        {
            Assert.Equal(192, terminal.VideoFrames); Assert.Equal(301, terminal.AudioFrames);
            Assert.Equal(308_224, terminal.AudioSamples); Assert.Equal(1, terminal.Completions);
            Assert.False(terminal.Disposed);
        });
        Assert.True(terminal.Disposed);
        Assert.Equal(1, terminal.Generations);
        output.WriteLine("FAKE TERMINAL PASS: same live PCP/native-decoder runner; PHYSICAL=NOT RUN.");
    }

    [FfmpegNativeAcceptanceFact]
    public async Task Pinned_runtime_and_Windows_harness_compose_without_initializing_output()
    {
        using var runtime = await EncodingAcceptanceRuntime.LocateAsync();
        var abi = new PlaybackDecoderFfmpegAbiSymbolGate().Acquire(runtime);
        Assert.Equal(PlaybackDecoderFfmpegAbiSymbolGateResult.Verified, abi.Result);
        abi.Lease?.Dispose();
        var dryIdentity = new LivePresentationIdentity("HARNESS-DRY-TASK", "HARNESS-DRY-RUN",
            new string('a', 40), Path.GetTempPath());
        dryIdentity.Validate();
        await using var session = new LiveWindowsPresentationSession(dryIdentity, output);
        Assert.Equal(Windows.PlaybackWindowsPhysicalRunnerState.Created, session.RunnerSnapshot.State);
        Assert.False(session.RunnerSnapshot.HasMessageLoop);
        Assert.Equal(0, session.RunnerSnapshot.WindowToken);
        Assert.Throws<InvalidOperationException>(() => session.Output);
        Assert.Contains(dryIdentity.RunId, dryIdentity.RunnerOptions.Title, StringComparison.Ordinal);
        Assert.False(session.CompletedCaptured);
        output.WriteLine("DRY GATE PASS: pinned runtime/ABI, runner configuration and terminal types available; StartAsync NOT CALLED; guard NOT ARMED; physical initializations=0.");
    }

    [LiveWindowsPhysicalAcceptanceFact]
    public async Task Paced_live_Pcp_native_decode_presents_through_existing_Windows_output_once()
    {
        // Repeat authorization in the body: direct invocation cannot bypass discovery gating.
        Assert.Equal("RUN-ONCE", Environment.GetEnvironmentVariable("NOVA_LIVE_PHYSICAL_RUN_ONCE"));
        var identity = new LivePresentationIdentity(
            Environment.GetEnvironmentVariable("NOVA_LIVE_PHYSICAL_TASK_ID")!,
            Environment.GetEnvironmentVariable("NOVA_LIVE_PHYSICAL_RUN_ID")!,
            Environment.GetEnvironmentVariable("NOVA_LIVE_PHYSICAL_HEAD")!,
            Environment.GetEnvironmentVariable("NOVA_LIVE_PHYSICAL_GUARD_DIRECTORY")!);
        identity.Validate();
        output.WriteLine($"Task-ID: {identity.TaskId}; Run-ID: {identity.RunId}; HEAD: {identity.Head}");
        using var runtime = await EncodingAcceptanceRuntime.LocateAsync(); // no Windows output
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        LiveWindowsPresentationSession? session = null;
        await LiveWindowsPresentationHarness.RunOnceAsync(identity,
            id => session = new LiveWindowsPresentationSession(id, output),
            (terminal, completed, token) => new LiveEncodingNativeAcceptanceTests(output)
                .RunAsync(true, terminal, runtime, completed, token), deadline.Token);
        Assert.NotNull(session);
        Assert.True(session.CompletedCaptured);
        Assert.Equal(Windows.PlaybackWindowsPhysicalRunnerState.Disposed, session.RunnerSnapshot.State);
        Assert.False(session.RunnerSnapshot.HasMessageLoop);
        Assert.Equal(0, session.RunnerSnapshot.WindowToken);
        output.WriteLine("AUTOMATED LIVE PHYSICAL PIPELINE COMPLETE; human motion/audio NOT RECORDED.");
    }
}
