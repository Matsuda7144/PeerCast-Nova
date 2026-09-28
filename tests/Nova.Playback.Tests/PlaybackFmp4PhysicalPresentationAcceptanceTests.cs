using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Windows;
using Xunit.Abstractions;

namespace PeerCast.Nova.Playback.Tests;

public sealed class Fmp4PcpLoopbackPhysicalAcceptanceFactAttribute : FactAttribute
{
    public const string RunOnceEnvironmentVariable =
        "NOVA_FMP4_PCP_LOOPBACK_RUN_ONCE";
    public const string ExpectedHeadEnvironmentVariable =
        "NOVA_FMP4_PCP_LOOPBACK_HEAD";

    public Fmp4PcpLoopbackPhysicalAcceptanceFactAttribute()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000) ||
            !Environment.Is64BitProcess)
        {
            Skip = "Requires the Windows 11 x64 Nova physical output host.";
            return;
        }

        if (!string.Equals(
                Environment.GetEnvironmentVariable(RunOnceEnvironmentVariable),
                "RUN-ONCE",
                StringComparison.Ordinal))
        {
            Skip = $"Set {RunOnceEnvironmentVariable}=RUN-ONCE for one explicit PCP loopback run.";
            return;
        }

        var expectedHead = Environment.GetEnvironmentVariable(
            ExpectedHeadEnvironmentVariable);
        if (expectedHead is null || expectedHead.Length != 40 ||
            !expectedHead.All(Uri.IsHexDigit))
        {
            Skip = $"Set {ExpectedHeadEnvironmentVariable} to the exact tested HEAD SHA.";
            return;
        }

        var runtimeDirectory = Environment.GetEnvironmentVariable(
            PlaybackFmp4FfmpegNativeAcceptanceTests.RuntimeDirectoryEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(runtimeDirectory) ||
            !Directory.Exists(runtimeDirectory))
        {
            Skip = "Set NOVA_FMP4_FFMPEG_TEST_RUNTIME to the accepted #249 runtime inventory.";
        }
    }
}

public sealed class Fmp4PhysicalPresentationAcceptanceFactAttribute : FactAttribute
{
    public const string RunOnceEnvironmentVariable =
        "NOVA_FMP4_PHYSICAL_PRESENTATION_RUN_ONCE";
    public const string ExpectedHeadEnvironmentVariable =
        "NOVA_FMP4_PHYSICAL_PRESENTATION_HEAD";

    public Fmp4PhysicalPresentationAcceptanceFactAttribute()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000) ||
            !Environment.Is64BitProcess)
        {
            Skip = "Requires the Windows 11 x64 Nova physical output host.";
            return;
        }

        if (!string.Equals(
                Environment.GetEnvironmentVariable(RunOnceEnvironmentVariable),
                "RUN-ONCE",
                StringComparison.Ordinal))
        {
            Skip = $"Set {RunOnceEnvironmentVariable}=RUN-ONCE for one explicit physical run.";
            return;
        }

        var expectedHead = Environment.GetEnvironmentVariable(
            ExpectedHeadEnvironmentVariable);
        if (expectedHead is null || expectedHead.Length != 40 ||
            !expectedHead.All(Uri.IsHexDigit))
        {
            Skip = $"Set {ExpectedHeadEnvironmentVariable} to the exact tested HEAD SHA.";
            return;
        }

        var runtimeDirectory = Environment.GetEnvironmentVariable(
            PlaybackFmp4FfmpegNativeAcceptanceTests.RuntimeDirectoryEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(runtimeDirectory) ||
            !Directory.Exists(runtimeDirectory))
        {
            Skip = "Set NOVA_FMP4_FFMPEG_TEST_RUNTIME to the accepted #249 runtime inventory.";
        }
    }
}

public sealed class PlaybackFmp4PhysicalPresentationAcceptanceTests(
    ITestOutputHelper output)
{
    private const int FixtureSize = 14_777;
    private const string FixtureSha256 =
        "3780066C30D70C5063368D708C3850410CAD03840E0B302966206C94AA0425EC";
    private const int VideoWidth = 64;
    private const int VideoHeight = 48;
    private const int VideoStride = VideoWidth * 4;

    [Fmp4PhysicalPresentationAcceptanceFact]
    public async Task RealFixturePresentsDecodedFramesThroughNovaWindowsOutput()
    {
        var runId = $"FMP4-PRESENT-{DateTimeOffset.UtcNow:yyyyMMdd'T'HHmmssfff'Z'}-" +
            Guid.NewGuid().ToString("N")[..8];
        var fixture = ReadFixture();
        Assert.Equal(FixtureSize, fixture.Length);
        Assert.Equal(FixtureSha256, Convert.ToHexString(SHA256.HashData(fixture)));
        var (initializationSegment, mediaFragments) = SplitFixture(fixture);
        var runtimeDirectory = Environment.GetEnvironmentVariable(
            PlaybackFmp4FfmpegNativeAcceptanceTests.RuntimeDirectoryEnvironmentVariable)!;
        var runtimeResult = await PlaybackDecoderBackendInstalledRuntimeLocator
            .LocateAndVerifyAsync(runtimeDirectory, CreateRuntimeManifest().RuntimeInventory);
        Assert.Equal(
            PlaybackDecoderBackendInstalledRuntimeLocation.Verified,
            runtimeResult.Location);
        using var runtimeLease = Assert.IsType<VerifiedPlaybackDecoderRuntimeLease>(
            runtimeResult.Lease);
        var abi = new PlaybackDecoderFfmpegAbiSymbolGate().Acquire(runtimeLease);
        Assert.Equal(PlaybackDecoderFfmpegAbiSymbolGateResult.Verified, abi.Result);
        abi.Lease?.Dispose();

        WriteLine($"Run-ID: {runId}");
        WriteLine($"HEAD: {GetExpectedHead()}");
        WriteLine($"Fixture: {fixture.Length} bytes SHA-256 {FixtureSha256}");
        WriteLine("Runtime: btbn.ffmpeg.8.1.3.autobuild.2026-09-26-13-03; " +
            "n8.1.3-20260926; host-attested/unreviewed");
        WriteLine("Human observation required during this run: visible moving test pattern; audible 440 Hz tone.");

        var runner = new PlaybackWindowsPhysicalRunnerHost(new()
        {
            ClientWidth = 640,
            ClientHeight = 360,
            Title = $"PeerCast Nova fMP4 physical presentation {runId}",
            MaximumPendingOperations = 4,
        });
        using var runTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var runToken = runTimeout.Token;
        PresentationResources? resources = null;
        var runnerStopped = false;
        try
        {
            var started = await runner.StartAsync(runToken);
            Assert.Equal(PlaybackWindowsPhysicalRunnerResult.Started, started);
            var runnerAtStart = runner.Snapshot;
            Assert.Equal(PlaybackWindowsPhysicalRunnerState.Running, runnerAtStart.State);
            Assert.True(runnerAtStart.WindowToken > 0);
            Assert.True(runnerAtStart.HasMessageLoop);
            WriteLine($"Surface: HWND token {runnerAtStart.WindowToken}; 640x360; owner thread {runnerAtStart.OwnerManagedThreadId}");

            var initialized = await runner.InvokeAsync(
                cancellationToken => PresentationResources.CreateAsync(
                    runnerAtStart.WindowToken,
                    cancellationToken),
                runToken);
            Assert.Equal(PlaybackWindowsOutputDispatcherInvocationResult.Completed,
                initialized.Result);
            var outputResources = Assert.IsType<PresentationResources>(initialized.Value);
            resources = outputResources;

            using var timing = new PlaybackAvTimingDecisionBoundary(
                new StopwatchPlaybackMonotonicClock(),
                new PlaybackAvTimingTolerances(),
                videoGeneration: 0,
                audioGeneration: 0);
            await using var presentation = new PlaybackDecodedFrameWindowsPresentationAdapter(
                runner,
                outputResources.Sink,
                timing,
                videoGeneration: 0,
                audioGeneration: 0,
                resetOutput: async (videoGeneration, audioGeneration, token) =>
                {
                    var invocation = await runner.InvokeAsync(
                        async outputToken =>
                        {
                            var result = await outputResources.Reconfiguration.ReconfigureAsync(
                                videoGeneration,
                                audioGeneration,
                                outputToken);
                            return result ==
                                    PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult.Reconfigured &&
                                outputResources.Sink.CommitReconfiguration(
                                    videoGeneration,
                                    audioGeneration);
                        },
                        token);
                    return invocation.Result ==
                            PlaybackWindowsOutputDispatcherInvocationResult.Completed &&
                        invocation.Value;
                },
                readAudioPadding: async token =>
                {
                    var invocation = await runner.InvokeAsync(
                        _ => ValueTask.FromResult(outputResources.Output.ReadAudioPadding()),
                        token);
                    return invocation.Result ==
                            PlaybackWindowsOutputDispatcherInvocationResult.Completed
                        ? invocation.Value
                        : new(PlaybackWindowsAudioPaddingReadResult.Failure, 0);
                });
            var adapterResult = PlaybackFmp4LiveEncodedSampleBatchAdapter.Create(
                initializationSegment,
                out var adapter);
            Assert.Equal(PlaybackFmp4LiveEncodedSampleBatchAdapterCreateResult.Created,
                adapterResult);
            var ownedAdapter = Assert.IsType<PlaybackFmp4LiveEncodedSampleBatchAdapter>(
                adapter);
            var videoTrack = ownedAdapter.StreamDescription.TrackDescriptions.Single(
                static track => track.MediaRole == PlaybackEncodedMediaRole.Video);
            var audioTrack = ownedAdapter.StreamDescription.TrackDescriptions.Single(
                static track => track.MediaRole == PlaybackEncodedMediaRole.Audio);
            Assert.Equal(PlaybackEncodedCodecVariant.Avc1, videoTrack.CodecVariant);
            Assert.Equal(PlaybackEncodedCodecVariant.Aac, audioTrack.CodecVariant);
            var decoderFactory = new PlaybackFmp4FfmpegDecoderFactory(runtimeLease);
            var sessionResult = PlaybackFmp4LiveDecoderSession.Create(
                ownedAdapter.StreamDescription,
                decoderFactory,
                out var session,
                presentation);
            Assert.Equal(PlaybackFmp4LiveDecoderSessionCreateResult.Created, sessionResult);
            var ownedSession = Assert.IsType<PlaybackFmp4LiveDecoderSession>(session);
            var elapsed = Stopwatch.StartNew();
            await using (ownedSession.ConfigureAwait(false))
            {
                foreach (var fragment in mediaFragments)
                {
                    Assert.Equal(
                        PlaybackEncodedSampleBatchCreationResult.Created,
                        ownedAdapter.CreateBatch(fragment, out var batch));
                    using var ownedBatch = Assert.IsType<PlaybackEncodedSampleBatch>(batch);
                    Assert.Equal(PlaybackEncodedDiscontinuity.None, ownedBatch.Discontinuity);
                    await ownedSession.DecodeBatchAsync(ownedBatch, runToken);
                }

                await ownedSession.CompleteAsync(runToken);
            }

            var completion = await presentation.CompleteAsync(runToken);
            elapsed.Stop();
            var presented = presentation.Snapshot;
            await Task.Delay(TimeSpan.FromSeconds(5), runToken);
            var outputSnapshot = outputResources.Sink.Snapshot;
            var native = outputSnapshot.Output.NativeEvidence;
            WriteLine($"Playback elapsed: {elapsed.Elapsed}; fixture timeline: {presented.FirstVideoPresentationTimestamp} to {presented.LastVideoPresentationTimestamp}");
            WriteLine($"Video: submitted={presented.VideoFrameSubmittedCount}; DXGI Present acknowledged={native.VideoPresentAcknowledgementCount}; " +
                $"frames presented={presented.VideoPresentedCount}; late dropped={presented.LateVideoDropCount}; " +
                $"first/last PTS={presented.FirstVideoPresentationTimestamp}/{presented.LastVideoPresentationTimestamp}");
            WriteLine($"Audio: submitted={presented.AudioFrameSubmittedCount}; WASAPI writes acknowledged={native.AudioWriteAcknowledgementCount}; " +
                $"frames accepted={native.AudioAcceptedFrameCount}; sample format={native.AudioSampleFormat}; " +
                $"rate/channels={native.AudioSampleRate}/{native.AudioChannelCount}; " +
                $"first/last PTS={presented.FirstAudioPresentationTimestamp}/{presented.LastAudioPresentationTimestamp}");
            WriteLine($"Audio completion padding: {presented.AudioPaddingAtCompletion.Result}/" +
                $"{presented.AudioPaddingAtCompletion.PaddingFrameCount} frames");
            WriteLine("Human visual observation: not recorded by instrumentation; user report required.");
            WriteLine("Human audible observation: not recorded by instrumentation; user report required.");

            Assert.Equal(PlaybackDecodedFrameWindowsPresentationCompletionResult.Completed,
                completion);
            Assert.Equal(PlaybackDecodedFrameWindowsPresentationState.Completed, presented.State);
            Assert.True(presented.VideoPresentedCount >= 2);
            Assert.True(presented.AudioWrittenCount >= 2);
            Assert.Equal(1, presented.MaximumOutputInFlight);
            Assert.True(presented.FirstVideoPresentationTimestamp.HasValue);
            Assert.True(presented.LastVideoPresentationTimestamp.HasValue);
            Assert.True(presented.FirstAudioPresentationTimestamp.HasValue);
            Assert.True(presented.LastAudioPresentationTimestamp.HasValue);
            Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(500));
            Assert.True(
                presented.LastVideoPresentationTimestamp!.Value -
                    presented.FirstVideoPresentationTimestamp!.Value >=
                TimeSpan.FromMilliseconds(500));
            Assert.True(
                presented.LastAudioPresentationTimestamp!.Value -
                    presented.FirstAudioPresentationTimestamp!.Value >=
                TimeSpan.FromMilliseconds(500));
            Assert.Equal(outputSnapshot.ProcessedVideoCount,
                presented.VideoPresentedCount);
            Assert.Equal(outputSnapshot.ProcessedAudioCount,
                presented.AudioWrittenCount);
            Assert.Equal(PlaybackControlledWindowsOutputEvidenceSource.SystemWindows,
                native.Source);
            Assert.False(native.CounterOverflowed);
            Assert.False(native.VideoConfigurationConflict);
            Assert.False(native.AudioConfigurationConflict);
            Assert.Equal(PlaybackDecodedVideoPixelFormat.Bgra32, native.VideoPixelFormat);
            Assert.Equal(VideoWidth, native.VideoWidth);
            Assert.Equal(VideoHeight, native.VideoHeight);
            Assert.True(native.VideoPresentAcknowledgementCount >= 2);
            Assert.Equal(presented.VideoPresentedCount,
                native.VideoPresentAcknowledgementCount);
            Assert.Equal(PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved,
                native.AudioSampleFormat);
            Assert.Equal(48_000, native.AudioSampleRate);
            Assert.Equal(2, native.AudioChannelCount);
            Assert.True(native.AudioStartAcknowledgementCount > 0);
            Assert.True(native.AudioWriteAcknowledgementCount >= 2);
            Assert.Equal(presented.AudioWrittenCount,
                native.AudioWriteAcknowledgementCount);
            Assert.True(native.AudioAcceptedFrameCount > 0);
            Assert.Equal(0, native.VideoDeviceLostCount);
            Assert.Equal(0, native.AudioDeviceLostCount);
            Assert.Equal(PlaybackWindowsAudioPaddingReadResult.Available,
                presented.AudioPaddingAtCompletion.Result);
            Assert.Equal(0, presented.AudioPaddingAtCompletion.PaddingFrameCount);

            var runnerBeforeCleanup = runner.Snapshot;
            Assert.Equal(PlaybackWindowsPhysicalRunnerState.Running,
                runnerBeforeCleanup.State);
            Assert.Equal(runnerAtStart.WindowToken, runnerBeforeCleanup.WindowToken);
            Assert.True(runnerBeforeCleanup.HasMessageLoop);

            var cleanup = await runner.InvokeAsync(
                async cancellationToken =>
                {
                    await resources.DisposeAsync();
                    return resources.CleanupSnapshot;
                },
                CancellationToken.None);
            Assert.Equal(PlaybackWindowsOutputDispatcherInvocationResult.Completed,
                cleanup.Result);
            Assert.Equal(
                PlaybackIsoBmffRegularMovieWindowsOutputSinkState.Disposed,
                cleanup.Value.SinkState);
            Assert.Equal(PlaybackWindowsOutputBufferState.Disposed,
                cleanup.Value.VideoBufferState);
            Assert.Equal(PlaybackWindowsOutputBufferState.Disposed,
                cleanup.Value.AudioBufferState);
            Assert.Equal(PlaybackWindowsOutputStreamState.Disposed,
                cleanup.Value.VideoStreamState);
            Assert.Equal(PlaybackWindowsOutputStreamState.Disposed,
                cleanup.Value.AudioStreamState);
            Assert.Equal(PlaybackWindowsOutputBindingState.Disposed,
                cleanup.Value.VideoBindingState);
            Assert.Equal(PlaybackWindowsOutputBindingState.Disposed,
                cleanup.Value.AudioBindingState);
            Assert.Equal(PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationState.Disposed,
                cleanup.Value.ReconfigurationState);
            resources = null;

            var stop = await runner.StopAsync();
            runnerStopped = true;
            Assert.Equal(PlaybackWindowsPhysicalRunnerResult.Stopped, stop);
            Assert.Equal(PlaybackWindowsPhysicalRunnerState.Stopped, runner.Snapshot.State);
            Assert.False(runner.Snapshot.HasMessageLoop);
            WriteLine($"Lifecycle: completed/drained; output resources released; runner stopped; invocation elapsed={elapsed.Elapsed}");
        }
        finally
        {
            if (resources is not null && runner.Snapshot.State ==
                    PlaybackWindowsPhysicalRunnerState.Running)
            {
                await runner.InvokeAsync(
                    async _ =>
                    {
                        await resources.DisposeAsync();
                        return true;
                    },
                    CancellationToken.None);
            }

            if (!runnerStopped)
            {
                await runner.StopAsync();
            }

            await runner.DisposeAsync();
        }
    }

    [Fmp4PcpLoopbackPhysicalAcceptanceFact]
    public async Task RealPcpLoopbackFixturePresentsThroughNativeFfmpegAndNovaWindowsOutput()
    {
        var runId = $"FMP4-PCP-LOOPBACK-{DateTimeOffset.UtcNow:yyyyMMdd'T'HHmmssfff'Z'}-" +
            Guid.NewGuid().ToString("N")[..8];
        var expectedHead = Environment.GetEnvironmentVariable(
            Fmp4PcpLoopbackPhysicalAcceptanceFactAttribute.ExpectedHeadEnvironmentVariable)!;
        Assert.Equal(expectedHead, ReadRepositoryHead());

        var fixture = Fmp4PcpLoopbackAcceptanceFixture.Load();
        var runtimeDirectory = Environment.GetEnvironmentVariable(
            PlaybackFmp4FfmpegNativeAcceptanceTests.RuntimeDirectoryEnvironmentVariable)!;
        var runtimeManifest = CreateRuntimeManifest();
        var runtimeResult = await PlaybackDecoderBackendInstalledRuntimeLocator
            .LocateAndVerifyAsync(runtimeDirectory, runtimeManifest.RuntimeInventory);
        Assert.Equal(
            PlaybackDecoderBackendInstalledRuntimeLocation.Verified,
            runtimeResult.Location);
        using var runtimeLease = Assert.IsType<VerifiedPlaybackDecoderRuntimeLease>(
            runtimeResult.Lease);
        var abi = new PlaybackDecoderFfmpegAbiSymbolGate().Acquire(runtimeLease);
        Assert.Equal(PlaybackDecoderFfmpegAbiSymbolGateResult.Verified, abi.Result);
        abi.Lease?.Dispose();

        var channelGuid = Guid.ParseExact("642ED9E0B91349E0ABCE5B4E6EC1CC38", "N");
        var sourceSessionGuid = Guid.ParseExact("8A54CA9872E84D18B26940BF1375F224", "N");
        var channelId = new ChannelId(channelGuid);
        var sourceSessionId = new ChannelSessionId(sourceSessionGuid);
        var packetPlan = Fmp4PcpLoopbackAcceptanceFixture.CreatePacketPlan(
            fixture,
            channelId,
            sourceSessionId);
        var windowRunner = new PlaybackWindowsPhysicalRunnerHost(new()
        {
            ClientWidth = 640,
            ClientHeight = 360,
            Title = $"PeerCast Nova PCP fMP4 loopback {runId}",
            MaximumPendingOperations = 4,
        });
        using var runTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var runToken = runTimeout.Token;
        PresentationResources? resources = null;
        WindowsLoopbackPresentationFactory? presentationFactory = null;
        PcpRelayFanout? fanout = null;
        PcpDownstreamRelayServer? server = null;
        PlaybackPcpFmp4LiveSessionRunner? liveRunner = null;
        CancellationTokenSource? receiverStop = null;
        Task<PlaybackPcpFmp4LiveSessionRunResult>? receiverTask = null;
        Task<PcpDownstreamRelayServerResult>? serverTask = null;
        var windowRunnerStopped = false;

        WriteLine($"Run-ID: {runId}");
        WriteLine($"HEAD: {expectedHead}");
        WriteLine($"Fixture: {fixture.Bytes.Length} bytes SHA-256 {Fmp4PcpLoopbackAcceptanceFixture.ExpectedSha256}");
        WriteLine($"FFmpeg runtime: {runtimeManifest.BuildId}; {runtimeManifest.Version}; host-attested/unreviewed");
        WriteLine($"Runtime ABI/symbol gate: {abi.Result}; verified files={runtimeLease.FileCount}");
        WriteLine("Human observation required during this run: visible moving test pattern; audible 440 Hz tone.");

        try
        {
            var started = await windowRunner.StartAsync(runToken);
            Assert.Equal(PlaybackWindowsPhysicalRunnerResult.Started, started);
            var windowAtStart = windowRunner.Snapshot;
            Assert.Equal(PlaybackWindowsPhysicalRunnerState.Running, windowAtStart.State);
            Assert.True(windowAtStart.WindowToken > 0);
            Assert.True(windowAtStart.HasMessageLoop);
            WriteLine($"Nova output surface: HWND token {windowAtStart.WindowToken}; 640x360; owner thread {windowAtStart.OwnerManagedThreadId}");

            var initialized = await windowRunner.InvokeAsync(
                cancellationToken => PresentationResources.CreateAsync(
                    windowAtStart.WindowToken,
                    cancellationToken),
                runToken);
            Assert.Equal(
                PlaybackWindowsOutputDispatcherInvocationResult.Completed,
                initialized.Result);
            resources = Assert.IsType<PresentationResources>(initialized.Value);
            presentationFactory = new WindowsLoopbackPresentationFactory(
                windowRunner,
                resources);
            var decoderFactory = new RecordingNativeDecoderFactory(
                new PlaybackFmp4FfmpegDecoderFactory(runtimeLease));

            fanout = new PcpRelayFanout();
            server = new PcpDownstreamRelayServer(
                new IPEndPoint(IPAddress.Loopback, 0),
                fanout,
                channelId,
                sourceSessionGuid);
            serverTask = server.RunAsync();
            var endpoint = server.LocalEndPoint;
            var sourceUri = $"pcp://127.0.0.1:{endpoint.Port}/channel/{channelGuid:N}";
            Assert.True(PlaybackPcpFmp4LiveSource.TryParse(sourceUri, out var source));
            WriteLine($"Source endpoint: {endpoint}; URI: {sourceUri}");
            WriteLine($"Source identity: ChannelId={channelGuid:N}; SessionId={sourceSessionGuid:N}");
            WriteLine($"Packet plan: HEAD=2, DATA=4, total={packetPlan.Count}, payload={packetPlan.Sum(static packet => packet.Payload.Length)} bytes; split init at byte 5, first moof at byte 11, cross-fragment DATA packet");

            liveRunner = new PlaybackPcpFmp4LiveSessionRunner(
                Assert.IsType<PlaybackPcpFmp4LiveSource>(source),
                decoderFactory,
                presentationFactory,
                new PlaybackPcpFmp4LiveSessionOptions
                {
                    MaximumRunDuration = TimeSpan.FromSeconds(15),
                    ConnectTimeout = TimeSpan.FromSeconds(3),
                    Session = new PcpChannelSessionOptions
                    {
                        HandshakeTimeout = TimeSpan.FromSeconds(5),
                        MaxReconnectAttempts = 0,
                        ReconnectDelay = TimeSpan.Zero,
                    },
                });
            receiverStop = new CancellationTokenSource();
            receiverTask = liveRunner.RunAsync(receiverStop.Token);
            await WaitForLoopbackChildRegistrationAsync(
                fanout,
                receiverTask,
                runToken);
            WriteLine($"PCP child subscription ready: active children={fanout.ActiveChildCount}");

            foreach (var packet in packetPlan)
            {
                await fanout.ConsumeAsync(packet, runToken);
            }

            Assert.True(await fanout.WaitForIdleAsync(
                TimeSpan.FromSeconds(5),
                runToken));
            await presentationFactory.WaitForMeaningfulOutputAsync(runToken);
            var receiver = await receiverTask.WaitAsync(runToken);
            Assert.True(receiver.DurationLimitReached);
            Assert.False(receiver.CallerCancelled);
            Assert.Null(receiver.CompletionFailure);
            var session = Assert.IsType<PcpChannelSessionResult>(receiver.Session);
            Assert.Equal(PcpChannelSessionEndReason.Stopped, session.EndReason);

            await server.StopAsync(runToken);
            var serverResult = await serverTask.WaitAsync(runToken);
            var serverSnapshot = serverResult.Snapshot;
            var composition = receiver.Composition;
            var presentation = Assert.Single(presentationFactory.Adapters).Snapshot;
            var outputSnapshot = resources.Sink.Snapshot;
            var native = outputSnapshot.Output.NativeEvidence;
            var paddingRead = await windowRunner.InvokeAsync(
                _ => ValueTask.FromResult(resources.Output.ReadAudioPadding()),
                runToken);
            Assert.Equal(
                PlaybackWindowsOutputDispatcherInvocationResult.Completed,
                paddingRead.Result);

            WriteLine($"Source counters: packets={packetPlan.Count}; HEAD=2; DATA=4; payload={fixture.Bytes.Length} bytes");
            WriteLine($"PCP server: state/end={serverSnapshot.State}/{serverResult.EndReason}; accepted connections={serverSnapshot.AcceptedConnectionCount}; accepted handshakes={serverSnapshot.AcceptedHandshakeCount}; delivered packets/bytes={serverSnapshot.DeliveredPacketCount}/{serverSnapshot.DeliveredByteCount}; discarded={serverSnapshot.DiscardedPacketCount}/{serverSnapshot.DiscardedByteCount}; active={serverSnapshot.ActiveConnectionCount}; cleanup incomplete={serverSnapshot.CleanupIncompleteCount}; internal failures={serverSnapshot.InternalFailureCount}");
            WriteLine($"Receiver: end={session.EndReason}; channel messages={session.ChannelMessageCount}; packets={session.PacketCount}; payload={session.PayloadByteCount}; attempts/reconnects={session.AttemptCount}/{session.ReconnectCount}");
            WriteLine($"#251 composition: result={composition.Result}; accepted/head/data={composition.AcceptedPacketCount}/{composition.HeadPacketCount}/{composition.DataPacketCount}; payload={composition.PayloadByteCount}; init generations={composition.InitializationGenerationCount}; segments={composition.MediaSegmentCount}; batches={composition.BatchCount}; decoder submissions={composition.DecoderSubmissionCount}; discontinuities={composition.DiscontinuityCount}; carry={composition.AssemblerCarryBytes}");
            WriteLine($"Native decoder: configured tracks={string.Join(",", decoderFactory.ConfiguredTracks.Select(static track => $"{track.MediaRole}:{track.CodecVariant}"))}; H.264/AAC samples={decoderFactory.VideoSampleCount}/{decoderFactory.AudioSampleCount}; decoded video/audio frames={presentation.VideoFrameSubmittedCount}/{presentation.AudioFrameSubmittedCount}; resets={decoderFactory.ResetCount}; failures={decoderFactory.FailureCount}; disposed={decoderFactory.DisposeCount}");
            WriteLine($"Video output: D3D11 Present acknowledgements={native.VideoPresentAcknowledgementCount}; presented={presentation.VideoPresentedCount}; late drops={presentation.LateVideoDropCount}; first/last PTS={presentation.FirstVideoPresentationTimestamp}/{presentation.LastVideoPresentationTimestamp}; sink state/result={outputSnapshot.State}/{outputSnapshot.Result}; device lost={native.VideoDeviceLostCount}");
            WriteLine($"Audio output: WASAPI writes={native.AudioWriteAcknowledgementCount}; written={presentation.AudioWrittenCount}; format={native.AudioSampleFormat}; rate/channels={native.AudioSampleRate}/{native.AudioChannelCount}; late delivery={presentation.LateAudioDeliveryCount}; final padding={paddingRead.Value.Result}/{paddingRead.Value.PaddingFrameCount}; sink state/result={outputSnapshot.State}/{outputSnapshot.Result}; device lost={native.AudioDeviceLostCount}");
            WriteLine("Human visual observation: not recorded by instrumentation; direct user report required.");
            WriteLine("Human audible observation: not recorded by instrumentation; direct user report required.");

            Assert.Equal(1, serverSnapshot.AcceptedConnectionCount);
            Assert.Equal(1, serverSnapshot.AcceptedHandshakeCount);
            Assert.Equal(packetPlan.Count, serverSnapshot.DeliveredPacketCount);
            Assert.Equal(fixture.Bytes.Length, serverSnapshot.DeliveredByteCount);
            Assert.Equal(0, serverSnapshot.DiscardedPacketCount);
            Assert.Equal(0, serverSnapshot.ProtocolRejectedCount);
            Assert.Equal(0, serverSnapshot.WriteFailedCount);
            Assert.Equal(0, serverSnapshot.CleanupIncompleteCount);
            Assert.Equal(0, serverSnapshot.InternalFailureCount);
            Assert.Equal(0, serverSnapshot.ActiveConnectionCount);
            Assert.Equal(packetPlan.Count, session.ChannelMessageCount);
            Assert.Equal(packetPlan.Count, session.PacketCount);
            Assert.Equal(fixture.Bytes.Length, session.PayloadByteCount);
            Assert.Equal(packetPlan.Count, composition.AcceptedPacketCount);
            Assert.Equal(2, composition.HeadPacketCount);
            Assert.Equal(4, composition.DataPacketCount);
            Assert.Equal(fixture.Bytes.Length, composition.PayloadByteCount);
            Assert.Equal(1, composition.InitializationGenerationCount);
            Assert.Equal(2, composition.MediaSegmentCount);
            Assert.Equal(2, composition.BatchCount);
            Assert.Equal(2, composition.DecoderSubmissionCount);
            Assert.Equal(0, composition.DiscontinuityCount);
            Assert.Equal(0, composition.AssemblerCarryBytes);
            Assert.Equal(PlaybackPcpFmp4CompositionState.Accepting, composition.State);
            Assert.Equal(PlaybackPcpFmp4CompositionResult.PacketAccepted,
                composition.Result);
            Assert.Single(decoderFactory.Decoders);
            Assert.True(decoderFactory.VideoSampleCount > 0);
            Assert.True(decoderFactory.AudioSampleCount > 0);
            Assert.Equal(0, decoderFactory.FailureCount);
            Assert.Equal(0, decoderFactory.ResetCount);
            Assert.Equal(0, decoderFactory.CompleteCount);
            Assert.Equal(1, decoderFactory.DisposeCount);
            Assert.Equal(0, Assert.Single(decoderFactory.Decoders).CompleteCount);
            Assert.Equal(2, decoderFactory.ConfiguredTracks.Count);
            Assert.Contains(decoderFactory.ConfiguredTracks, static track =>
                track.MediaRole == PlaybackEncodedMediaRole.Video &&
                track.CodecVariant == PlaybackEncodedCodecVariant.Avc1);
            Assert.Contains(decoderFactory.ConfiguredTracks, static track =>
                track.MediaRole == PlaybackEncodedMediaRole.Audio &&
                track.CodecVariant == PlaybackEncodedCodecVariant.Aac);
            Assert.True(presentation.VideoFrameSubmittedCount > 0);
            Assert.True(presentation.AudioFrameSubmittedCount > 0);
            Assert.Equal(PlaybackDecodedFrameWindowsPresentationState.Disposed,
                presentation.State);
            Assert.True(presentation.VideoPresentedCount >= 2);
            Assert.True(presentation.AudioWrittenCount >= 2);
            Assert.True(native.VideoPresentAcknowledgementCount >= 2);
            Assert.Equal(presentation.VideoPresentedCount,
                native.VideoPresentAcknowledgementCount);
            Assert.True(native.AudioWriteAcknowledgementCount >= 2);
            Assert.Equal(presentation.AudioWrittenCount,
                native.AudioWriteAcknowledgementCount);
            Assert.Equal(PlaybackDecodedVideoPixelFormat.Bgra32, native.VideoPixelFormat);
            Assert.Equal(PlaybackControlledWindowsOutputEvidenceSource.SystemWindows,
                native.Source);
            Assert.Equal(64, native.VideoWidth);
            Assert.Equal(48, native.VideoHeight);
            Assert.False(native.CounterOverflowed);
            Assert.False(native.VideoConfigurationConflict);
            Assert.False(native.AudioConfigurationConflict);
            Assert.Equal(PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved,
                native.AudioSampleFormat);
            Assert.Equal(48_000, native.AudioSampleRate);
            Assert.Equal(2, native.AudioChannelCount);
            Assert.True(native.AudioStartAcknowledgementCount > 0);
            Assert.Equal(0, native.VideoDeviceLostCount);
            Assert.Equal(0, native.AudioDeviceLostCount);
            Assert.Equal(PlaybackIsoBmffRegularMovieWindowsOutputSinkState.Accepting,
                outputSnapshot.State);
            Assert.NotEqual(PlaybackIsoBmffRegularMovieWindowsOutputSinkResult.Faulted,
                outputSnapshot.Result);

            var cleanup = await windowRunner.InvokeAsync(
                async cancellationToken =>
                {
                    await resources.DisposeAsync();
                    return resources.CleanupSnapshot;
                },
                CancellationToken.None);
            Assert.Equal(PlaybackWindowsOutputDispatcherInvocationResult.Completed,
                cleanup.Result);
            Assert.Equal(PlaybackIsoBmffRegularMovieWindowsOutputSinkState.Disposed,
                cleanup.Value.SinkState);
            Assert.Equal(PlaybackWindowsOutputBufferState.Disposed,
                cleanup.Value.VideoBufferState);
            Assert.Equal(PlaybackWindowsOutputBufferState.Disposed,
                cleanup.Value.AudioBufferState);
            Assert.Equal(PlaybackWindowsOutputStreamState.Disposed,
                cleanup.Value.VideoStreamState);
            Assert.Equal(PlaybackWindowsOutputStreamState.Disposed,
                cleanup.Value.AudioStreamState);
            Assert.Equal(PlaybackWindowsOutputBindingState.Disposed,
                cleanup.Value.VideoBindingState);
            Assert.Equal(PlaybackWindowsOutputBindingState.Disposed,
                cleanup.Value.AudioBindingState);
            Assert.Equal(PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationState.Disposed,
                cleanup.Value.ReconfigurationState);
            resources = null;

            var stop = await windowRunner.StopAsync();
            windowRunnerStopped = true;
            Assert.Equal(PlaybackWindowsPhysicalRunnerResult.Stopped, stop);
            Assert.Equal(PlaybackWindowsPhysicalRunnerState.Stopped,
                windowRunner.Snapshot.State);
            Assert.False(windowRunner.Snapshot.HasMessageLoop);
            WriteLine("Cleanup: receiver/session disposed; PCP server stopped; Windows output resources released; owner thread stopped.");
        }
        finally
        {
            receiverStop?.Cancel();
            if (receiverTask is { IsCompleted: false })
            {
                try
                {
                    await receiverTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException)
                {
                    // The one physical attempt has been stopped and is not retried.
                }
            }

            if (liveRunner is not null)
            {
                await liveRunner.DisposeAsync();
            }

            if (server is not null)
            {
                await server.StopAsync();
            }

            if (fanout is not null)
            {
                await fanout.DisposeAsync();
            }

            presentationFactory?.Dispose();
            if (resources is not null && windowRunner.Snapshot.State ==
                    PlaybackWindowsPhysicalRunnerState.Running)
            {
                await windowRunner.InvokeAsync(
                    async _ =>
                    {
                        await resources.DisposeAsync();
                        return true;
                    },
                    CancellationToken.None);
            }

            if (!windowRunnerStopped)
            {
                await windowRunner.StopAsync();
            }

            await windowRunner.DisposeAsync();
            receiverStop?.Dispose();
        }
    }

    private static byte[] ReadFixture()
    {
        using var stream = typeof(PlaybackFmp4PhysicalPresentationAcceptanceTests)
            .Assembly
            .GetManifestResourceStream(
                "PeerCast.Nova.Playback.Tests.Fixtures.fmp4-avc1-aac.mp4");
        Assert.NotNull(stream);
        using var ownedStream = stream!;
        using var bytes = new MemoryStream();
        ownedStream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static string ReadRepositoryHead()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null &&
            !File.Exists(Path.Combine(current.FullName, ".git")) &&
            !Directory.Exists(Path.Combine(current.FullName, ".git")))
        {
            current = current.Parent;
        }

        if (current is null)
        {
            throw new InvalidOperationException(
                "The loopback acceptance test could not locate its Git worktree.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = current.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("rev-parse");
        startInfo.ArgumentList.Add("HEAD");
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "Git could not start for the exact-head physical acceptance gate.");
        var head = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(5_000) || process.ExitCode != 0 ||
            head.Length != 40 || !head.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException(
                $"The exact-head physical acceptance gate failed: {error.Trim()}");
        }

        return head;
    }

    private static async Task WaitForLoopbackChildRegistrationAsync(
        PcpRelayFanout fanout,
        Task receiverTask,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        while (fanout.ActiveChildCount != 1)
        {
            if (receiverTask.IsCompleted)
            {
                await receiverTask.ConfigureAwait(false);
                throw new InvalidOperationException(
                    "The receiver stopped before the loopback PCP child registered.");
            }

            if (!await timer.WaitForNextTickAsync(cancellationToken)
                    .ConfigureAwait(false))
            {
                throw new TimeoutException(
                    "The loopback PCP child did not register before the deadline.");
            }
        }
    }

    private static PlaybackFfmpegExternalRuntimeManifest CreateRuntimeManifest()
    {
        var creation = PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied(
            "btbn.ffmpeg.8.1.3.autobuild.2026-09-26-13-03",
            "n8.1.3-20260926",
            PlaybackFfmpegExternalRuntimeManifest.RequiredDeclaredLicenseIdentifier,
            PlaybackFfmpegExternalRuntimeBuildConfiguration.Required,
            [
                new(
                    PlaybackFfmpegExternalRuntimeLibraryRole.AvCodec,
                    "avcodec-62.dll",
                    62,
                    90_991_616,
                    "6B5E1673069C80A56CA0E13239280A65D7D2BAABC57777C58E898E3770C0B9C4"),
                new(
                    PlaybackFfmpegExternalRuntimeLibraryRole.AvFormat,
                    "avformat-62.dll",
                    62,
                    22_722_048,
                    "460FB6AEDDB48B3A4310EC8CB53EEDD8F54C72C986E8CEF6444A3FD257C81CCC"),
                new(
                    PlaybackFfmpegExternalRuntimeLibraryRole.AvUtil,
                    "avutil-60.dll",
                    60,
                    3_013_120,
                    "28CB4FC673DD0FF47BB66D0A33DE29CB44ED906CA52E50DBE65D5F71135247E0"),
                new(
                    PlaybackFfmpegExternalRuntimeLibraryRole.SwResample,
                    "swresample-6.dll",
                    6,
                    734_720,
                    "106A35D51FEB30E0F72887E87058F0F49497FAB00350EC0EBDE9F32C72F93222"),
                new(
                    PlaybackFfmpegExternalRuntimeLibraryRole.SwScale,
                    "swscale-9.dll",
                    9,
                    12_900_864,
                    "6F7FFA0C8EBF31DD807ECB96E03D52D44783A889A8D819E5B5F4AC3585C86AAA"),
            ]);
        return Assert.IsType<PlaybackFfmpegExternalRuntimeManifest>(creation.Manifest);
    }

    private static (byte[] InitializationSegment, IReadOnlyList<byte[]> MediaFragments)
        SplitFixture(ReadOnlyMemory<byte> fixture)
    {
        var bytes = fixture.Span;
        var boxes = new List<TopLevelBox>();
        var offset = 0;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 8)
            {
                throw new InvalidDataException("The fixture has a truncated top-level box.");
            }

            var size = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            var type = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset + 4, 4));
            var headerSize = 8;
            ulong boxSize = size;
            if (size == 1)
            {
                if (bytes.Length - offset < 16)
                {
                    throw new InvalidDataException("The fixture has a truncated large box.");
                }

                boxSize = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(offset + 8, 8));
                headerSize = 16;
            }
            else if (size == 0)
            {
                boxSize = checked((ulong)(bytes.Length - offset));
            }

            if (boxSize < (ulong)headerSize || boxSize > (ulong)(bytes.Length - offset))
            {
                throw new InvalidDataException("The fixture has an invalid top-level box length.");
            }

            boxes.Add(new(offset, checked((int)boxSize), type));
            offset = checked(offset + (int)boxSize);
        }

        var firstMediaIndex = boxes.FindIndex(static box => box.Type == FourCc("moof"));
        if (firstMediaIndex <= 0 ||
            !boxes.Take(firstMediaIndex).Any(static box => box.Type == FourCc("ftyp")) ||
            !boxes.Take(firstMediaIndex).Any(static box => box.Type == FourCc("moov")))
        {
            throw new InvalidDataException("The fixture is missing its fMP4 initialization boxes.");
        }

        var initialization = bytes[..boxes[firstMediaIndex].Offset].ToArray();
        var fragments = new List<byte[]>();
        for (var index = firstMediaIndex; index < boxes.Count;)
        {
            var moof = boxes[index];
            if (moof.Type != FourCc("moof") || index + 1 >= boxes.Count ||
                boxes[index + 1].Type != FourCc("mdat"))
            {
                throw new InvalidDataException("The fixture has an unsupported media-fragment layout.");
            }

            var mdat = boxes[index + 1];
            var end = checked(mdat.Offset + mdat.Length);
            fragments.Add(bytes[moof.Offset..end].ToArray());
            index += 2;
        }

        Assert.Equal(2, fragments.Count);
        return (initialization, fragments.AsReadOnly());
    }

    private static uint FourCc(string value) =>
        BinaryPrimitives.ReadUInt32BigEndian(Encoding.ASCII.GetBytes(value));

    private static string GetExpectedHead() =>
        Environment.GetEnvironmentVariable(
            Fmp4PhysicalPresentationAcceptanceFactAttribute.ExpectedHeadEnvironmentVariable)!;

    private void WriteLine(string message)
    {
        output.WriteLine(message);
        Console.WriteLine(message);
    }

    private readonly record struct TopLevelBox(int Offset, int Length, uint Type);

    private sealed class WindowsLoopbackPresentationFactory :
        IPlaybackPcpFmp4PresentationOutputGenerationFactory,
        IDisposable
    {
        private readonly object _gate = new();
        private readonly PlaybackWindowsPhysicalRunnerHost _runner;
        private readonly PresentationResources _resources;
        private readonly List<PlaybackDecodedFrameWindowsPresentationAdapter> _adapters = [];
        private readonly List<PlaybackAvTimingDecisionBoundary> _timings = [];

        public WindowsLoopbackPresentationFactory(
            PlaybackWindowsPhysicalRunnerHost runner,
            PresentationResources resources)
        {
            _runner = runner;
            _resources = resources;
        }

        public IReadOnlyList<PlaybackDecodedFrameWindowsPresentationAdapter> Adapters
        {
            get
            {
                lock (_gate)
                {
                    return [.. _adapters];
                }
            }
        }

        public IPlaybackPcpFmp4PresentationOutputGeneration Create(long generation)
        {
            var timing = new PlaybackAvTimingDecisionBoundary(
                new StopwatchPlaybackMonotonicClock(),
                new PlaybackAvTimingTolerances(),
                videoGeneration: generation,
                audioGeneration: generation);
            var adapter = new PlaybackDecodedFrameWindowsPresentationAdapter(
                _runner,
                _resources.Sink,
                timing,
                generation,
                generation,
                resetOutput: async (videoGeneration, audioGeneration, token) =>
                {
                    var invocation = await _runner.InvokeAsync(
                        async outputToken =>
                        {
                            var result = await _resources.Reconfiguration.ReconfigureAsync(
                                videoGeneration,
                                audioGeneration,
                                outputToken);
                            return result ==
                                    PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult.Reconfigured &&
                                _resources.Sink.CommitReconfiguration(
                                    videoGeneration,
                                    audioGeneration);
                        },
                        token);
                    return invocation.Result ==
                            PlaybackWindowsOutputDispatcherInvocationResult.Completed &&
                        invocation.Value;
                },
                readAudioPadding: async token =>
                {
                    var invocation = await _runner.InvokeAsync(
                        _ => ValueTask.FromResult(_resources.Output.ReadAudioPadding()),
                        token);
                    return invocation.Result ==
                            PlaybackWindowsOutputDispatcherInvocationResult.Completed
                        ? invocation.Value
                        : new(PlaybackWindowsAudioPaddingReadResult.Failure, 0);
                });

            lock (_gate)
            {
                _timings.Add(timing);
                _adapters.Add(adapter);
            }

            return new PlaybackPcpFmp4WindowsPresentationOutputGeneration(adapter);
        }

        public async Task WaitForMeaningfulOutputAsync(
            CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
            while (true)
            {
                foreach (var adapter in Adapters)
                {
                    var snapshot = adapter.Snapshot;
                    var videoSpan = GetSpan(
                        snapshot.FirstVideoPresentationTimestamp,
                        snapshot.LastVideoPresentationTimestamp);
                    var audioSpan = GetSpan(
                        snapshot.FirstAudioPresentationTimestamp,
                        snapshot.LastAudioPresentationTimestamp);
                    if (snapshot.VideoPresentedCount >= 2 &&
                        snapshot.AudioWrittenCount >= 2 &&
                        videoSpan >= TimeSpan.FromMilliseconds(500) &&
                        audioSpan >= TimeSpan.FromMilliseconds(500))
                    {
                        return;
                    }
                }

                if (!await timer.WaitForNextTickAsync(cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new TimeoutException(
                        "Nova did not present both fMP4 tracks before the physical run deadline.");
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                foreach (var timing in _timings)
                {
                    timing.Dispose();
                }

                _timings.Clear();
            }
        }

        private static TimeSpan GetSpan(TimeSpan? first, TimeSpan? last) =>
            first is { } start && last is { } end
                ? end - start
                : TimeSpan.Zero;
    }

    private sealed class RecordingNativeDecoderFactory(
        IPlaybackDecoderFactory inner) : IPlaybackDecoderFactory
    {
        public List<PlaybackEncodedTrackDescription> ConfiguredTracks { get; } = [];

        public List<RecordingNativeDecoder> Decoders { get; } = [];

        public int VideoSampleCount => Decoders.Sum(static decoder => decoder.VideoSampleCount);

        public int AudioSampleCount => Decoders.Sum(static decoder => decoder.AudioSampleCount);

        public int ResetCount => Decoders.Sum(static decoder => decoder.ResetCount);

        public int CompleteCount => Decoders.Sum(static decoder => decoder.CompleteCount);

        public int FailureCount { get; private set; }

        public int DisposeCount => Decoders.Sum(static decoder => decoder.DisposeCount);

        public IPlaybackDecoder? Create(PlaybackDecoderCreationContext context)
        {
            try
            {
                if (context.EncodedStreamDescription is { } description)
                {
                    ConfiguredTracks.AddRange(description.TrackDescriptions);
                }

                var decoder = inner.Create(context);
                if (decoder is null)
                {
                    FailureCount++;
                    return null;
                }

                var recordingDecoder = new RecordingNativeDecoder(decoder, this);
                Decoders.Add(recordingDecoder);
                return recordingDecoder;
            }
            catch
            {
                FailureCount++;
                throw;
            }
        }

        public void RecordFailure() => FailureCount++;
    }

    private sealed class RecordingNativeDecoder(
        IPlaybackDecoder inner,
        RecordingNativeDecoderFactory owner) : IPlaybackDecoder
    {
        public int VideoSampleCount { get; private set; }

        public int AudioSampleCount { get; private set; }

        public int ResetCount { get; private set; }

        public int CompleteCount { get; private set; }

        public int DisposeCount { get; private set; }

        public async ValueTask DecodeAsync(
            PlaybackEncodedInput input,
            CancellationToken cancellationToken)
        {
            try
            {
                var metadata = input.Fmp4SampleMetadata ??
                    throw new InvalidOperationException(
                        "A native fMP4 decoder submission has no track metadata.");
                if (metadata.MediaRole == PlaybackEncodedMediaRole.Video)
                {
                    VideoSampleCount++;
                }
                else if (metadata.MediaRole == PlaybackEncodedMediaRole.Audio)
                {
                    AudioSampleCount++;
                }

                await inner.DecodeAsync(input, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                owner.RecordFailure();
                throw;
            }
        }

        public async ValueTask ResetAsync(CancellationToken cancellationToken)
        {
            try
            {
                ResetCount++;
                await inner.ResetAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                owner.RecordFailure();
                throw;
            }
        }

        public async ValueTask CompleteAsync(CancellationToken cancellationToken)
        {
            try
            {
                CompleteCount++;
                await inner.CompleteAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                owner.RecordFailure();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            await inner.DisposeAsync().ConfigureAwait(false);
        }
    }

    private readonly record struct PresentationCleanupSnapshot(
        PlaybackIsoBmffRegularMovieWindowsOutputSinkState SinkState,
        PlaybackWindowsOutputBufferState VideoBufferState,
        PlaybackWindowsOutputBufferState AudioBufferState,
        PlaybackWindowsOutputStreamState VideoStreamState,
        PlaybackWindowsOutputStreamState AudioStreamState,
        PlaybackWindowsOutputBindingState VideoBindingState,
        PlaybackWindowsOutputBindingState AudioBindingState,
        PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationState ReconfigurationState);

    private sealed class PresentationResources : IAsyncDisposable
    {
        private bool _disposed;

        private PresentationResources(
            PlaybackWindowsOutputActivationBoundary activation,
            PlaybackWindowsOutputResourceBindingBoundary binding,
            PlaybackWindowsOutputStreamInitializationBoundary streams,
            PlaybackWindowsOutputBufferProcessingBoundary output,
            PlaybackIsoBmffRegularMovieWindowsOutputSink sink,
            PlaybackIsoBmffRegularMovieWindowsOutputReconfiguration reconfiguration)
        {
            Activation = activation;
            Binding = binding;
            Streams = streams;
            Output = output;
            Sink = sink;
            Reconfiguration = reconfiguration;
        }

        public PlaybackWindowsOutputActivationBoundary Activation { get; }

        public PlaybackWindowsOutputResourceBindingBoundary Binding { get; }

        public PlaybackWindowsOutputStreamInitializationBoundary Streams { get; }

        public PlaybackWindowsOutputBufferProcessingBoundary Output { get; }

        public PlaybackIsoBmffRegularMovieWindowsOutputSink Sink { get; }

        public PlaybackIsoBmffRegularMovieWindowsOutputReconfiguration Reconfiguration { get; }

        public PresentationCleanupSnapshot CleanupSnapshot => new(
            Sink.Snapshot.State,
            Output.Snapshot.VideoState,
            Output.Snapshot.AudioState,
            Streams.Snapshot.VideoState,
            Streams.Snapshot.AudioState,
            Binding.Snapshot.VideoState,
            Binding.Snapshot.AudioState,
            Reconfiguration.Snapshot.State);

        public static async ValueTask<PresentationResources> CreateAsync(
            long windowToken,
            CancellationToken cancellationToken)
        {
            PlaybackWindowsOutputActivationBoundary? activation = null;
            PlaybackWindowsOutputResourceBindingBoundary? binding = null;
            PlaybackWindowsOutputStreamInitializationBoundary? streams = null;
            PlaybackWindowsOutputBufferProcessingBoundary? output = null;
            PlaybackIsoBmffRegularMovieWindowsOutputSink? sink = null;
            PlaybackIsoBmffRegularMovieWindowsOutputReconfiguration? reconfiguration = null;
            try
            {
                activation = new();
                if (activation.Activate() != PlaybackWindowsOutputActivationResult.Activated)
                {
                    throw new InvalidOperationException("Nova could not activate its Windows output devices.");
                }

                var configuration = new PlaybackIsoBmffRegularMovieWindowsOutputConfiguration(
                    new(
                        PlaybackDecodedVideoPixelFormat.Bgra32,
                        VideoWidth,
                        VideoHeight,
                        VideoStride,
                        0),
                    new(
                        PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved,
                        48_000,
                        2,
                        0),
                    new(
                        windowToken,
                        VideoWidth,
                        VideoHeight,
                        PlaybackDecodedVideoPixelFormat.Bgra32,
                        0),
                    new(
                        PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved,
                        48_000,
                        2,
                        TimeSpan.FromMilliseconds(100),
                        0));
                binding = new(activation);
                if (await binding.ConfigureAsync(
                        configuration.VideoBinding,
                        configuration.AudioBinding,
                        cancellationToken) != PlaybackWindowsOutputBindingResult.Bound)
                {
                    throw new InvalidOperationException("Nova could not bind the video surface and audio endpoint.");
                }

                streams = new(activation, binding);
                if (await streams.InitializeAsync(
                        configuration.VideoStream,
                        configuration.AudioStream,
                        cancellationToken) != PlaybackWindowsOutputStreamInitializationResult.Initialized)
                {
                    throw new InvalidOperationException("Nova could not initialize the Windows output streams.");
                }

                output = new(activation, binding, streams);
                var sinkCreation = new PlaybackIsoBmffRegularMovieWindowsOutputSinkFactory()
                    .Create(output, 0, 0);
                if (sinkCreation.Result !=
                    PlaybackIsoBmffRegularMovieWindowsOutputSinkCreationResult.Created)
                {
                    throw new InvalidOperationException($"Nova's decoded-frame output sink was unavailable: {sinkCreation.Result}.");
                }

                sink = Assert.IsType<PlaybackIsoBmffRegularMovieWindowsOutputSink>(
                    sinkCreation.Sink);
                var reconfigurationCreation =
                    PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationFactory.Create(
                        binding,
                        streams,
                        output,
                        configuration);
                if (reconfigurationCreation.Result !=
                    PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationCreationResult.Created)
                {
                    throw new InvalidOperationException($"Nova's output reset boundary was unavailable: {reconfigurationCreation.Result}.");
                }

                reconfiguration = Assert.IsType<
                    PlaybackIsoBmffRegularMovieWindowsOutputReconfiguration>(
                    reconfigurationCreation.Reconfiguration);
                return new(activation, binding, streams, output, sink, reconfiguration);
            }
            catch
            {
                reconfiguration?.Dispose();
                if (sink is not null)
                {
                    await sink.DisposeAsync();
                }

                output?.Dispose();
                streams?.Dispose();
                binding?.Dispose();
                activation?.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Reconfiguration.Dispose();
            await Sink.DisposeAsync();
            Output.Dispose();
            Streams.Dispose();
            Binding.Dispose();
            Activation.Dispose();
        }
    }
}
