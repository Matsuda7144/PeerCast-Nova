using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;
using PeerCast.Nova.Network.PortMapping;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisSharedPcpPlaybackRuntimeTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
    private static readonly Guid ChannelGuid = Guid.ParseExact(
        "531DC8DFC7FB42928AC2C0A626517A87",
        "N");
    private static readonly Guid RemoteSessionGuid = Guid.ParseExact(
        "102132435465768798a9babbdcddedef",
        "N");

    [Fact]
    public async Task SameChannelPlaybackAttachmentsShareOneApplicationOwnedUpstream()
    {
        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0));
        listener.Start();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var accepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionCount = 0;
        var serverTask = RunOneUpstreamAsync(
            listener,
            accepted,
            () => Interlocked.Increment(ref connectionCount),
            releaseSource.Task,
            timeout.Token);
        var composition = new PcpRecordingRuntimeComposition(
            Path.Combine(Path.GetTempPath(), "PeerCastNova-shared-owner-tests"));
        var runtime = new GenesisSharedPcpPlaybackRuntime(composition);
        var source = new DailyPcpPlaybackSource(
            IPAddress.Loopback.ToString(),
            listener.LocalEndPoint.Port,
            new ChannelId(ChannelGuid));

        using var firstAttachCancellation = new CancellationTokenSource();
        var first = await runtime.AttachPlaybackAsync(
            source,
            static (_, _) => new NoOpPlaybackConsumer(),
            firstAttachCancellation.Token);
        await accepted.Task.WaitAsync(timeout.Token);
        firstAttachCancellation.Cancel();
        var second = await runtime.AttachPlaybackAsync(
            source,
            static (_, _) => new NoOpPlaybackConsumer(),
            timeout.Token);

        Assert.Same(first.OwnerCompletion, second.OwnerCompletion);
        Assert.Equal(1, Volatile.Read(ref connectionCount));
        Assert.False(first.OwnerCompletion.IsCompleted);

        await first.Registration.DisposeAsync();
        await second.Registration.DisposeAsync();
        var result = await first.OwnerCompletion.WaitAsync(timeout.Token);
        await first.OwnerRetirement.WaitAsync(timeout.Token);
        releaseSource.TrySetResult();
        await serverTask.WaitAsync(timeout.Token);
        Assert.Equal(1, Volatile.Read(ref connectionCount));
        Assert.Equal(PcpRelayCoordinatorEndReason.Stopped, result.EndReason);
        Assert.Equal(0, runtime.RetainedOwnerCount);
        Assert.Equal(0, composition.RelayRuntime.OwnedCoordinatorCount);

        await runtime.DisposeAsync();
        await composition.DisposeAsync();
    }

    [Fact]
    public async Task CompletedPlaybackOwnerIsRetiredWithoutSameChannelReattach()
    {
        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0));
        listener.Start();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var accepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = RunOneUpstreamAsync(
            listener,
            accepted,
            static () => { },
            releaseSource.Task,
            timeout.Token);
        var composition = new PcpRecordingRuntimeComposition(
            Path.Combine(Path.GetTempPath(), "PeerCastNova-completed-owner-retirement-tests"));
        var runtime = new GenesisSharedPcpPlaybackRuntime(composition);
        GenesisSharedPcpPlaybackAttachment? attachment = null;

        try
        {
            attachment = await runtime.AttachPlaybackAsync(
                new DailyPcpPlaybackSource(
                    IPAddress.Loopback.ToString(),
                    listener.LocalEndPoint.Port,
                    new ChannelId(ChannelGuid)),
                static (_, _) => new NoOpPlaybackConsumer(),
                timeout.Token);
            await accepted.Task.WaitAsync(timeout.Token);

            await attachment.Registration.DisposeAsync();
            var result = await attachment.OwnerCompletion.WaitAsync(timeout.Token);
            await attachment.OwnerRetirement.WaitAsync(timeout.Token);
            Assert.Equal(PcpRelayCoordinatorEndReason.Stopped, result.EndReason);
            Assert.Equal(0, runtime.RetainedOwnerCount);
            Assert.Equal(0, composition.RelayRuntime.OwnedCoordinatorCount);
        }
        finally
        {
            releaseSource.TrySetResult();
            if (attachment is not null)
            {
                await attachment.OwnerRetirement.WaitAsync(timeout.Token);
            }

            await serverTask.WaitAsync(timeout.Token);
            await runtime.DisposeAsync();
            await composition.DisposeAsync();
        }
    }

    [Fact]
    public async Task SequentialDistinctChannelsDoNotAccumulateCompletedOwners()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var composition = new PcpRecordingRuntimeComposition(
            Path.Combine(Path.GetTempPath(), "PeerCastNova-distinct-owner-retirement-tests"));
        var runtime = new GenesisSharedPcpPlaybackRuntime(composition);
        Guid[] channelGuids =
        [
            Guid.ParseExact("11111111111141118111111111111111", "N"),
            Guid.ParseExact("22222222222242228222222222222222", "N"),
            Guid.ParseExact("33333333333343338333333333333333", "N"),
        ];

        try
        {
            foreach (var channelGuid in channelGuids)
            {
                await using var listener = new TcpTransportListener(
                    new IPEndPoint(IPAddress.Loopback, 0));
                listener.Start();
                var accepted = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseSource = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var serverTask = RunOneUpstreamAsync(
                    listener,
                    accepted,
                    static () => { },
                    releaseSource.Task,
                    timeout.Token,
                    channelGuid);
                GenesisSharedPcpPlaybackAttachment? attachment = null;

                try
                {
                    attachment = await runtime.AttachPlaybackAsync(
                        new DailyPcpPlaybackSource(
                            IPAddress.Loopback.ToString(),
                            listener.LocalEndPoint.Port,
                            new ChannelId(channelGuid)),
                        static (_, _) => new NoOpPlaybackConsumer(),
                        timeout.Token);
                    await accepted.Task.WaitAsync(timeout.Token);
                    Assert.Equal(1, runtime.RetainedOwnerCount);
                    Assert.Equal(1, composition.RelayRuntime.OwnedCoordinatorCount);

                    await attachment.Registration.DisposeAsync();
                    await attachment.OwnerCompletion.WaitAsync(timeout.Token);
                    await attachment.OwnerRetirement.WaitAsync(timeout.Token);

                    Assert.Equal(0, runtime.RetainedOwnerCount);
                    Assert.Equal(0, composition.RelayRuntime.OwnedCoordinatorCount);
                }
                finally
                {
                    releaseSource.TrySetResult();
                    if (attachment is not null)
                    {
                        await attachment.OwnerRetirement.WaitAsync(timeout.Token);
                    }

                    await serverTask.WaitAsync(timeout.Token);
                }
            }
        }
        finally
        {
            await runtime.DisposeAsync();
            await composition.DisposeAsync();
        }
    }

    [Fact]
    public async Task CompletionAndReattachRacePreservesExactlyOneOwner()
    {
        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0));
        listener.Start();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var acceptedCount = 0;
        var firstAccepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstServerTask = RunOneUpstreamAsync(
            listener,
            firstAccepted,
            () => Interlocked.Increment(ref acceptedCount),
            releaseFirst.Task,
            timeout.Token);
        var composition = new PcpRecordingRuntimeComposition(
            Path.Combine(Path.GetTempPath(), "PeerCastNova-owner-reattach-race-tests"));
        var runtime = new GenesisSharedPcpPlaybackRuntime(composition);
        GenesisSharedPcpPlaybackAttachment? first = null;
        GenesisSharedPcpPlaybackAttachment? second = null;
        using var secondAcceptCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        Task? secondServerTask = null;
        var releaseReplacement = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var source = new DailyPcpPlaybackSource(
                IPAddress.Loopback.ToString(),
                listener.LocalEndPoint.Port,
                new ChannelId(ChannelGuid));
            first = await runtime.AttachPlaybackAsync(
                source,
                static (_, _) => new NoOpPlaybackConsumer(),
                timeout.Token);
            await firstAccepted.Task.WaitAsync(timeout.Token);
            var secondAccepted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            secondServerTask = RunOneUpstreamAsync(
                listener,
                secondAccepted,
                () => Interlocked.Increment(ref acceptedCount),
                releaseReplacement.Task,
                secondAcceptCancellation.Token);

            var firstReleaseTask = first.Registration.DisposeAsync().AsTask();
            var secondAttachTask = runtime.AttachPlaybackAsync(
                source,
                static (_, _) => new NoOpPlaybackConsumer(),
                timeout.Token);
            second = await secondAttachTask.WaitAsync(timeout.Token);

            if (ReferenceEquals(first.OwnerCompletion, second.OwnerCompletion))
            {
                secondAcceptCancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => secondServerTask.WaitAsync(timeout.Token));
                Assert.Equal(1, Volatile.Read(ref acceptedCount));
                Assert.False(first.OwnerCompletion.IsCompleted);
            }
            else
            {
                await secondAccepted.Task.WaitAsync(timeout.Token);
                await first.OwnerCompletion.WaitAsync(timeout.Token);
                await first.OwnerRetirement.WaitAsync(timeout.Token);
                Assert.Equal(2, Volatile.Read(ref acceptedCount));
            }

            await firstReleaseTask.WaitAsync(timeout.Token);
            Assert.Equal(1, runtime.RetainedOwnerCount);
            Assert.Equal(1, composition.RelayRuntime.OwnedCoordinatorCount);
            Assert.False(second.OwnerCompletion.IsCompleted);

            await second.Registration.DisposeAsync();
            await second.OwnerCompletion.WaitAsync(timeout.Token);
            await second.OwnerRetirement.WaitAsync(timeout.Token);
            Assert.Equal(0, runtime.RetainedOwnerCount);
            Assert.Equal(0, composition.RelayRuntime.OwnedCoordinatorCount);
        }
        finally
        {
            releaseFirst.TrySetResult();
            releaseReplacement.TrySetResult();
            secondAcceptCancellation.Cancel();
            if (first is not null)
            {
                await first.Registration.DisposeAsync();
                await first.Consumer.DisposeAsync();
                await first.OwnerRetirement.WaitAsync(timeout.Token);
            }

            if (second is not null)
            {
                await second.Registration.DisposeAsync();
                await second.Consumer.DisposeAsync();
                await second.OwnerRetirement.WaitAsync(timeout.Token);
            }

            if (secondServerTask is not null)
            {
                try
                {
                    await secondServerTask.WaitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                    when (secondAcceptCancellation.IsCancellationRequested)
                {
                    // The concurrent attach reused the original live owner.
                }
            }

            await firstServerTask.WaitAsync(timeout.Token);
            await runtime.DisposeAsync();
            await composition.DisposeAsync();
        }
    }

    [Fact]
    public async Task TerminalUpstreamFailureRetiresTheCompletedOwner()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var unavailablePortProbe = new System.Net.Sockets.TcpListener(
            IPAddress.Loopback,
            0);
        unavailablePortProbe.Start();
        var unavailablePort = ((IPEndPoint)unavailablePortProbe.LocalEndpoint).Port;
        unavailablePortProbe.Stop();
        var composition = new PcpRecordingRuntimeComposition(
            Path.Combine(Path.GetTempPath(), "PeerCastNova-failed-owner-retirement-tests"));
        var runtime = new GenesisSharedPcpPlaybackRuntime(composition);

        GenesisSharedPcpPlaybackAttachment? attachment = null;
        try
        {
            attachment = await runtime.AttachPlaybackAsync(
                new DailyPcpPlaybackSource(
                    IPAddress.Loopback.ToString(),
                    unavailablePort,
                    new ChannelId(ChannelGuid)),
                static (_, _) => new NoOpPlaybackConsumer(),
                timeout.Token);

            var result = await attachment.OwnerCompletion.WaitAsync(timeout.Token);
            await attachment.OwnerRetirement.WaitAsync(timeout.Token);

            Assert.Equal(PcpRelayCoordinatorEndReason.UpstreamFailed, result.EndReason);
            Assert.Equal(0, runtime.RetainedOwnerCount);
            Assert.Equal(0, composition.RelayRuntime.OwnedCoordinatorCount);
        }
        finally
        {
            if (attachment is not null)
            {
                await attachment.Registration.DisposeAsync();
                await attachment.Consumer.DisposeAsync();
                await attachment.OwnerRetirement.WaitAsync(timeout.Token);
            }

            await runtime.DisposeAsync();
            await composition.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConcurrentRuntimeAndCompositionShutdownReleaseOwnerOnce()
    {
        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0));
        listener.Start();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var accepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = RunOneUpstreamAsync(
            listener,
            accepted,
            static () => { },
            releaseSource.Task,
            timeout.Token);
        var composition = new PcpRecordingRuntimeComposition(
            Path.Combine(Path.GetTempPath(), "PeerCastNova-shutdown-race-tests"));
        var runtime = new GenesisSharedPcpPlaybackRuntime(composition);
        GenesisSharedPcpPlaybackAttachment? attachment = null;

        try
        {
            attachment = await runtime.AttachPlaybackAsync(
                new DailyPcpPlaybackSource(
                    IPAddress.Loopback.ToString(),
                    listener.LocalEndPoint.Port,
                    new ChannelId(ChannelGuid)),
                static (_, _) => new NoOpPlaybackConsumer(),
                timeout.Token);
            await accepted.Task.WaitAsync(timeout.Token);

            await Task.WhenAll(
                runtime.DisposeAsync().AsTask(),
                composition.DisposeAsync().AsTask()).WaitAsync(timeout.Token);
            await attachment.OwnerCompletion.WaitAsync(timeout.Token);
            await attachment.OwnerRetirement.WaitAsync(timeout.Token);

            Assert.Equal(0, runtime.RetainedOwnerCount);
            Assert.Equal(0, composition.RelayRuntime.OwnedCoordinatorCount);
        }
        finally
        {
            releaseSource.TrySetResult();
            if (attachment is not null)
            {
                await attachment.OwnerRetirement.WaitAsync(timeout.Token);
            }

            await serverTask.WaitAsync(timeout.Token);
            await runtime.DisposeAsync();
            await composition.DisposeAsync();
        }
    }

    [Fact]
    public async Task PlaybackAttachmentFeedsHttpFlvBootstrapFromSharedUpstream()
    {
        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0));
        listener.Start();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var accepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = RunPlaybackBootstrapUpstreamAsync(
            listener,
            accepted,
            releaseHead.Task,
            releaseSource.Task,
            timeout.Token);
        var composition = new PcpRecordingRuntimeComposition(
            Path.Combine(Path.GetTempPath(), "PeerCastNova-playback-bootstrap-tests"));
        var runtime = new GenesisSharedPcpPlaybackRuntime(composition);
        var streamUrlReady = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new PcpHttpFlvPlaybackHost(
            streamUrlReady: url => streamUrlReady.TrySetResult(url));
        host.Start();
        GenesisSharedPcpPlaybackAttachment? attachment = null;

        try
        {
            attachment = await runtime.AttachPlaybackAsync(
                new DailyPcpPlaybackSource(
                    IPAddress.Loopback.ToString(),
                    listener.LocalEndPoint.Port,
                    new ChannelId(ChannelGuid)),
                host.Create,
                timeout.Token);
            await accepted.Task.WaitAsync(timeout.Token);
            releaseHead.TrySetResult();

            var streamUrl = await streamUrlReady.Task.WaitAsync(timeout.Token);
            Assert.Equal(
                host.StreamUrlFor(new ChannelId(ChannelGuid)),
                streamUrl);

            await using var streamConnection = await new TcpTransportConnector()
                .ConnectAsync(
                    IPAddress.Loopback.ToString(),
                    host.LocalEndPoint.Port,
                    timeout.Token);
            await streamConnection.WriteAsync(
                Encoding.ASCII.GetBytes(
                    $"GET /stream/{ChannelGuid:N} HTTP/1.1\r\n" +
                    "Host: 127.0.0.1\r\n\r\n"),
                timeout.Token);

            var responseHeader = await ReadHttpResponseHeaderAsync(
                streamConnection,
                timeout.Token);
            Assert.StartsWith("HTTP/1.1 200 OK\r\n", responseHeader);
            Assert.Contains("Content-Type: video/x-flv\r\n", responseHeader);
            Assert.Equal(
                CreateFlvHeader(),
                await ReadExactlyAsync(
                    streamConnection,
                    13,
                    timeout.Token));

            Assert.False(attachment.OwnerCompletion.IsCompleted);
        }
        finally
        {
            releaseHead.TrySetResult();
            if (attachment is not null)
            {
                await attachment.Registration.DisposeAsync();
                await attachment.Consumer.DisposeAsync();
                await attachment.OwnerCompletion.WaitAsync(timeout.Token);
            }

            releaseSource.TrySetResult();
            await serverTask.WaitAsync(timeout.Token);
            await host.DisposeAsync();
            await runtime.DisposeAsync();
            await composition.DisposeAsync();
        }
    }

    [Fact]
    public async Task DailyPlaybackPassesTheReadyRawFlvUrlToTheSelectedPlayer()
    {
        await using var listener = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Start();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = RunPlaybackBootstrapUpstreamAsync(
            listener, accepted, releaseHead.Task, releaseSource.Task, timeout.Token);
        var composition = new PcpRecordingRuntimeComposition(
            Path.Combine(Path.GetTempPath(), "PeerCastNova-daily-player-tests"));
        var launcher = new RecordingPlayerLauncher();
        var changedLauncher = new RecordingPlayerLauncher();
        var selectedLauncher = launcher;
        await using var controller = new NovaDailyHttpFlvPlaybackController(
            () => selectedLauncher, static _ => { }, static _ => { }, composition, new GenesisSharedPcpPlaybackRuntime(composition));

        try
        {
            var channel = new GenesisChannelItem("test FLV",
                $"pcp://127.0.0.1:{listener.LocalEndPoint.Port}/channel/{ChannelGuid:N}");
            var play = controller.PlayAsync(channel, timeout.Token);
            await accepted.Task.WaitAsync(timeout.Token);
            Assert.False(launcher.Launched.Task.IsCompleted);

            selectedLauncher = changedLauncher;
            releaseHead.SetResult();
            var source = await launcher.Launched.Task.WaitAsync(timeout.Token);
            await play.WaitAsync(timeout.Token);
            Assert.False(changedLauncher.Launched.Task.IsCompleted);
            var url = new Uri(source.StreamUrl);
            Assert.Equal("127.0.0.1", url.Host);
            Assert.Equal($"/stream/{ChannelGuid:N}", url.AbsolutePath, ignoreCase: true);
            Assert.Equal(url.Port, new Uri(source.PlaylistUrl).Port);

            await using var client = await new TcpTransportConnector().ConnectAsync(url.Host, url.Port, timeout.Token);
            await client.WriteAsync(Encoding.ASCII.GetBytes(
                $"GET {url.AbsolutePath} HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n"), timeout.Token);
            Assert.StartsWith("HTTP/1.1 200 OK\r\n", await ReadHttpResponseHeaderAsync(client, timeout.Token));
            Assert.Equal(CreateFlvHeader(), await ReadExactlyAsync(client, 13, timeout.Token));

            await controller.StopAsync().WaitAsync(timeout.Token);
            Assert.True(launcher.Completion.IsCompletedSuccessfully);
        }
        finally
        {
            releaseHead.TrySetResult();
            releaseSource.TrySetResult();
            await server.WaitAsync(timeout.Token);
        }
    }

    private sealed class RecordingPlayerLauncher : IDailyPlaybackPlayerLauncher, IDailyPlaybackPlayerLifetime
    {
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<DailyPlaybackPlayerSource> Launched { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _stopped.Task;

        public void ValidateConfiguration() { }

        public ValueTask<IDailyPlaybackPlayerLifetime> LaunchAsync(
            DailyPlaybackPlayerSource source, CancellationToken cancellationToken)
        {
            Launched.SetResult(source);
            return ValueTask.FromResult<IDailyPlaybackPlayerLifetime>(this);
        }

        public ValueTask DisposeAsync()
        {
            _stopped.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task RecordingOwnerStartedFirstIsReusedByPlayback()
    {
        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0));
        listener.Start();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var accepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionCount = 0;
        var serverTask = RunOneUpstreamAsync(
            listener,
            accepted,
            () => Interlocked.Increment(ref connectionCount),
            releaseSource.Task,
            timeout.Token);
        var composition = new PcpRecordingRuntimeComposition(
            Path.Combine(Path.GetTempPath(), "PeerCastNova-recording-owner-tests"));
        var runtime = new GenesisSharedPcpPlaybackRuntime(composition);
        var source = new DailyPcpPlaybackSource(
            IPAddress.Loopback.ToString(),
            listener.LocalEndPoint.Port,
            new ChannelId(ChannelGuid));
        GenesisSharedPcpRecordingOwnerAttachment? recordingAttachment = null;
        GenesisSharedPcpPlaybackAttachment? playbackAttachment = null;

        try
        {
            recordingAttachment = await runtime.AttachRecordingOwnerAsync(
                source,
                timeout.Token);
            await accepted.Task.WaitAsync(timeout.Token);
            Assert.Same(composition.Recording, recordingAttachment.Recording);
            Assert.False(recordingAttachment.OwnerCompletion.IsCompleted);

            playbackAttachment = await runtime.AttachPlaybackAsync(
                source,
                static (_, _) => new NoOpPlaybackConsumer(),
                timeout.Token);
            Assert.Same(
                recordingAttachment.OwnerCompletion,
                playbackAttachment.OwnerCompletion);
            Assert.Equal(1, Volatile.Read(ref connectionCount));

            await recordingAttachment.DisposeAsync();
            Assert.False(playbackAttachment.OwnerCompletion.IsCompleted);

            await playbackAttachment.Registration.DisposeAsync();
            var result = await playbackAttachment.OwnerCompletion
                .WaitAsync(timeout.Token);
            Assert.Equal(PcpRelayCoordinatorEndReason.Stopped, result.EndReason);
        }
        finally
        {
            if (recordingAttachment is not null)
            {
                await recordingAttachment.DisposeAsync();
            }

            if (playbackAttachment is not null)
            {
                await playbackAttachment.Registration.DisposeAsync();
                await playbackAttachment.Consumer.DisposeAsync();
            }

            releaseSource.TrySetResult();
            await serverTask.WaitAsync(timeout.Token);
            await runtime.DisposeAsync();
            await composition.DisposeAsync();
        }
    }

    [Fact]
    public async Task GuiNextManualRecordingUsesOneSharedOwnerAndDoesNotLaunchPlayer()
    {
        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0));
        listener.Start();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var accepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionCount = 0;
        var serverTask = RunOneUpstreamAsync(
            listener,
            accepted,
            () => Interlocked.Increment(ref connectionCount),
            releaseSource.Task,
            timeout.Token);
        var recordingDirectory = Path.Combine(
            Path.GetTempPath(),
            "PeerCastNova-gui-manual-recording-shared-owner-tests",
            Guid.NewGuid().ToString("N"));
        var composition = new PcpRecordingRuntimeComposition(recordingDirectory);
        var sharedRuntime = new GenesisSharedPcpPlaybackRuntime(composition);
        var dailyController = new NovaDailyHttpFlvPlaybackController(
            new PcrPlayerExternalPlayerLauncher(explicitPath: null),
            static _ => { },
            static _ => { },
            composition,
            sharedRuntime);
        var guiController = new GuiNextDailyPlaybackSessionController(dailyController);
        GenesisSharedPcpPlaybackAttachment? playbackAttachment = null;
        var guiDisposed = false;

        try
        {
            var channelId = new ChannelId(ChannelGuid);
            var source = new DailyPcpPlaybackSource(
                IPAddress.Loopback.ToString(),
                listener.LocalEndPoint.Port,
                channelId);
            var channel = new GenesisChannelItem(
                "selected station",
                $"pcp://127.0.0.1:{listener.LocalEndPoint.Port}/channel/{ChannelGuid:N}",
                Description: "managed fixture channel",
                BitrateKbps: 256,
                Type: "FLV",
                ChannelId: ChannelGuid.ToString("N").ToUpperInvariant());
            var viewModel = new GenesisMainViewModel(guiController);
            viewModel.ReplaceChannels([channel]);
            viewModel.SelectedChannel = channel;
            Assert.True(viewModel.GetManualRecordingPresentation(channel).CanRecord);

            var start = viewModel.StartManualRecordingAsync(channel, timeout.Token).AsTask();
            await accepted.Task.WaitAsync(timeout.Token);
            await start.WaitAsync(timeout.Token);

            Assert.Equal("録画中", viewModel.GetManualRecordingPresentation(channel).StatusText);
            Assert.True(composition.Recording.TryGetState(channelId, out var recordingState));
            Assert.Equal(RecordingSessionOrigin.Manual, recordingState!.Session!.Origin);
            Assert.Equal(1, sharedRuntime.RetainedOwnerCount);
            Assert.Equal(1, composition.RelayRuntime.OwnedCoordinatorCount);

            playbackAttachment = await sharedRuntime.AttachPlaybackAsync(
                source,
                static (_, _) => new NoOpPlaybackConsumer(),
                timeout.Token);
            Assert.Equal(1, Volatile.Read(ref connectionCount));
            Assert.False(playbackAttachment.OwnerCompletion.IsCompleted);
            Assert.Equal(1, sharedRuntime.RetainedOwnerCount);
            Assert.Equal(1, composition.RelayRuntime.OwnedCoordinatorCount);

            await viewModel.StopManualRecordingAsync(channel, timeout.Token);
            Assert.Equal(GenesisRecordingCoreStateKind.Idle,
                guiController.GetSnapshot(channel).State);
            Assert.False(playbackAttachment.OwnerCompletion.IsCompleted);
            Assert.Equal(1, sharedRuntime.RetainedOwnerCount);
            Assert.Equal(1, composition.RelayRuntime.OwnedCoordinatorCount);

            await viewModel.StartManualRecordingAsync(channel, timeout.Token);
            Assert.Equal("録画中", viewModel.GetManualRecordingPresentation(channel).StatusText);
            Assert.Equal(1, Volatile.Read(ref connectionCount));

            var sharedOwnerCompletion = playbackAttachment.OwnerCompletion;
            await playbackAttachment.Registration.DisposeAsync();
            await playbackAttachment.Consumer.DisposeAsync();
            Assert.False(sharedOwnerCompletion.IsCompleted);
            Assert.Equal(1, sharedRuntime.RetainedOwnerCount);
            Assert.Equal(1, composition.RelayRuntime.OwnedCoordinatorCount);

            await guiController.DisposeAsync();
            guiDisposed = true;
            var ownerResult = await sharedOwnerCompletion.WaitAsync(timeout.Token);
            await playbackAttachment.OwnerRetirement.WaitAsync(timeout.Token);
            Assert.Equal(PcpRelayCoordinatorEndReason.Stopped, ownerResult.EndReason);
            Assert.Equal(0, sharedRuntime.RetainedOwnerCount);
            Assert.Equal(0, composition.RelayRuntime.OwnedCoordinatorCount);
            Assert.Throws<ObjectDisposedException>(() =>
                composition.Recording.TryGetState(channelId, out _));
            Assert.Equal(1, Volatile.Read(ref connectionCount));
        }
        finally
        {
            releaseSource.TrySetResult();
            if (playbackAttachment is not null && !guiDisposed)
            {
                await playbackAttachment.Registration.DisposeAsync();
                await playbackAttachment.Consumer.DisposeAsync();
            }

            if (!guiDisposed)
            {
                await guiController.DisposeAsync();
            }
            try
            {
                await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Keep the primary assertion while still releasing the fixture listener.
            }

            if (Directory.Exists(recordingDirectory))
            {
                Directory.Delete(recordingDirectory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AutomaticRecordingUserStopSuppressesAndPreservesExactConsumerOwnership(bool playbackRemains)
    {
        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0));
        listener.Start();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var accepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionCount = 0;
        var serverTask = RunOneUpstreamAsync(
            listener,
            accepted,
            () => Interlocked.Increment(ref connectionCount),
            releaseSource.Task,
            timeout.Token);
        var recordingDirectory = Path.Combine(
            Path.GetTempPath(),
            "PeerCastNova-gui-automatic-recording-shared-owner-tests",
            Guid.NewGuid().ToString("N"));
        var composition = new PcpRecordingRuntimeComposition(recordingDirectory);
        var sharedRuntime = new GenesisSharedPcpPlaybackRuntime(composition);
        var channelId = new ChannelId(ChannelGuid);
        var source = new DailyPcpPlaybackSource(
            IPAddress.Loopback.ToString(),
            listener.LocalEndPoint.Port,
            channelId);
        var rule = new RecordingRule(
            Guid.NewGuid(),
            "automatic shared owner",
            RecordingRuleAction.Record,
            RecordingRuleCriteria.MatchAll);
        var dailyController = new NovaDailyHttpFlvPlaybackController(
            new PcrPlayerExternalPlayerLauncher(explicitPath: null),
            static _ => { },
            static _ => { },
            composition,
            sharedRuntime,
            new FixedRuleSetProvider(new([rule])));
        var guiController = new GuiNextDailyPlaybackSessionController(dailyController);
        var viewModel = new GenesisMainViewModel(guiController);
        GenesisSharedPcpPlaybackAttachment? playbackAttachment = null;
        GenesisChannelCatalogSession? catalogSession = null;
        var guiDisposed = false;

        try
        {
            playbackAttachment = await sharedRuntime.AttachPlaybackAsync(
                source,
                static (_, _) => new NoOpPlaybackConsumer(),
                timeout.Token);
            await accepted.Task.WaitAsync(timeout.Token);

            var channel = new GenesisChannelItem(
                "selected station",
                $"pcp://127.0.0.1:{listener.LocalEndPoint.Port}/channel/{ChannelGuid:N}",
                Description: "managed fixture channel",
                BitrateKbps: 256,
                Type: "FLV",
                ChannelId: ChannelGuid.ToString("N").ToUpperInvariant());
            Assert.NotNull(viewModel.AutomaticRecordingCatalogObserver);
            var catalog = new FixedChannelCatalog(channel);
            catalogSession = GenesisChannelCatalogSession.Create(
                catalog,
                viewModel);

            var refresh = await catalogSession.RefreshAsync(timeout.Token);
            Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, refresh);
            Assert.Single(viewModel.Channels);
            Assert.Equal(1, catalog.ReadCount);
            var projectedAutomatic = Assert.Single(viewModel.ActiveRecordings);
            Assert.Equal(RecordingSessionOrigin.AutomaticRule, projectedAutomatic.Origin);
            Assert.Equal(rule.Id, projectedAutomatic.AutomaticRuleId);
            Assert.Equal(channel.Name, projectedAutomatic.ChannelName);
            Assert.True(viewModel.GetActiveRecordingPresentation(projectedAutomatic).CanStop);
            Assert.True(composition.Recording.TryGetState(channelId, out var recordingState));
            var automaticSession = recordingState!.Session!;
            Assert.Equal(RecordingSessionOrigin.AutomaticRule, automaticSession.Origin);
            Assert.Equal(channelId, automaticSession.ChannelId);
            Assert.Equal(rule.Id, automaticSession.AutomaticRuleId);
            Assert.Equal(1, sharedRuntime.RetainedOwnerCount);
            Assert.Equal(1, composition.RelayRuntime.OwnedCoordinatorCount);
            Assert.Equal(1, Volatile.Read(ref connectionCount));
            Assert.False(playbackAttachment.OwnerCompletion.IsCompleted);

            catalog.SetChannels([]);
            var removalRefresh = await catalogSession.RefreshAsync(timeout.Token);
            Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, removalRefresh);
            Assert.Empty(viewModel.Channels);
            projectedAutomatic = Assert.Single(viewModel.ActiveRecordings);
            Assert.Equal(RecordingSessionOrigin.AutomaticRule, projectedAutomatic.Origin);
            Assert.Equal($"Channel {ChannelGuid.ToString("N")[..8].ToUpperInvariant()}",
                projectedAutomatic.ChannelName);
            Assert.Equal(rule.Id, projectedAutomatic.AutomaticRuleId);
            Assert.Equal(2, catalog.ReadCount);

            var repeatedRefresh = await catalogSession.RefreshAsync(timeout.Token);
            Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, repeatedRefresh);
            Assert.Equal(3, catalog.ReadCount);
            Assert.Equal(1, Volatile.Read(ref connectionCount));
            Assert.Equal(automaticSession.SessionId,
                composition.Recording.GetState(channelId).Session!.SessionId);

            if (!playbackRemains)
            {
                await playbackAttachment.Registration.DisposeAsync();
                await playbackAttachment.Consumer.DisposeAsync();
                Assert.False(playbackAttachment.OwnerCompletion.IsCompleted);
            }

            var suppressed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            viewModel.PropertyChanged += (_, _) =>
            {
                if (viewModel.SuppressionRows.Any(r => r.State == GenesisAutomaticRecordingSuppressionState.Committed))
                    suppressed.TrySetResult();
            };
            Assert.True(await GenesisGuiNextRecordingCommandDispatch.StopRecordingRowAsync(
                viewModel, new GenesisGuiNextNativeTabRow(channel, null, projectedAutomatic), timeout.Token));
            await suppressed.Task.WaitAsync(timeout.Token);
            var stoppedIntent = Assert.Single(viewModel.SuppressionRows);
            Assert.Equal(channelId, stoppedIntent.ChannelId);
            Assert.Equal(rule.Id, stoppedIntent.RuleId);
            Assert.True(viewModel.CanAllowAutomatic(stoppedIntent));
            Assert.False(await GenesisGuiNextRecordingCommandDispatch.StopRecordingRowAsync(
                viewModel, new GenesisGuiNextNativeTabRow(channel, null, projectedAutomatic), timeout.Token));
            await viewModel.RefreshActiveRecordingsAsync(timeout.Token);
            Assert.Empty(viewModel.ActiveRecordings);
            if (playbackRemains)
                Assert.False(playbackAttachment.OwnerCompletion.IsCompleted);
            else
                await playbackAttachment.OwnerRetirement.WaitAsync(timeout.Token);
            Assert.Equal(playbackRemains ? 1 : 0, sharedRuntime.RetainedOwnerCount);
            catalog.SetChannels([channel]);
            await catalogSession.RefreshAsync(timeout.Token);
            Assert.Null(composition.Recording.GetState(channelId).Session);
            Assert.Single(viewModel.SuppressionRows);
            Assert.Equal(1, Volatile.Read(ref connectionCount));
            await viewModel.AllowAutomaticRecordingAsync(stoppedIntent, timeout.Token);
            Assert.Empty(viewModel.SuppressionRows);
            Assert.Null(composition.Recording.GetState(channelId).Session);
            Assert.Equal(playbackRemains ? 1 : 0, sharedRuntime.RetainedOwnerCount);

            await catalogSession.DisposeAsync();
            catalogSession = null;
            await playbackAttachment.Registration.DisposeAsync();
            await playbackAttachment.Consumer.DisposeAsync();
            releaseSource.TrySetResult();

            var ownerResult = await playbackAttachment.OwnerCompletion.WaitAsync(timeout.Token);
            await playbackAttachment.OwnerRetirement.WaitAsync(timeout.Token);
            Assert.Equal(PcpRelayCoordinatorEndReason.Stopped, ownerResult.EndReason);
            Assert.Equal(0, sharedRuntime.RetainedOwnerCount);
            Assert.Equal(0, composition.RelayRuntime.OwnedCoordinatorCount);
            Assert.Equal(1, Volatile.Read(ref connectionCount));

            await guiController.DisposeAsync();
            guiDisposed = true;
        }
        finally
        {
            releaseSource.TrySetResult();
            if (catalogSession is not null)
            {
                await catalogSession.DisposeAsync();
            }

            if (playbackAttachment is not null)
            {
                await playbackAttachment.Registration.DisposeAsync();
                await playbackAttachment.Consumer.DisposeAsync();
            }

            if (!guiDisposed)
            {
                await guiController.DisposeAsync();
            }

            try
            {
                await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Preserve the primary assertion while releasing the fixture listener.
            }

            if (Directory.Exists(recordingDirectory))
            {
                Directory.Delete(recordingDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CanceledAttachDoesNotCreateAnUpstreamOwner()
    {
        var composition = new PcpRecordingRuntimeComposition(
            Path.Combine(Path.GetTempPath(), "PeerCastNova-canceled-owner-tests"));
        var runtime = new GenesisSharedPcpPlaybackRuntime(composition);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.AttachPlaybackAsync(
                new DailyPcpPlaybackSource(
                    IPAddress.Loopback.ToString(),
                    7144,
                    new ChannelId(ChannelGuid)),
                static (_, _) => new NoOpPlaybackConsumer(),
                canceled.Token));

        await runtime.DisposeAsync();
        Assert.Equal(0, runtime.RetainedOwnerCount);
        Assert.Equal(0, composition.RelayRuntime.OwnedCoordinatorCount);
        await composition.DisposeAsync();
    }

    private static async Task RunOneUpstreamAsync(
        TcpTransportListener listener,
        TaskCompletionSource accepted,
        Action onAccepted,
        Task releaseSource,
        CancellationToken cancellationToken,
        Guid? expectedChannelGuid = null)
    {
        var channelGuid = expectedChannelGuid ?? ChannelGuid;
        await using var connection = await listener.AcceptAsync(cancellationToken);
        onAccepted();

        var request = await ReadHttpRequestAsync(
            connection,
            cancellationToken);
        Assert.Contains(
            $"GET /channel/{channelGuid:N}",
            request,
            StringComparison.OrdinalIgnoreCase);
        await connection.WriteAsync(
            Encoding.ASCII.GetBytes(
                "HTTP/1.0 200 OK\r\n" +
                "Server: PeerCastStation/Test\r\n" +
                "Content-Type:application/x-peercast-pcp\r\n\r\n"),
            cancellationToken);

        var atoms = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(
            atoms,
            PcpAtom.Parent(
                PcpKnownAtomNames.Oleh,
                PcpAtom.Leaf(
                    PcpKnownAtomNames.HeloAgent,
                    PcpWireValueCodec.EncodeNullTerminatedUtf8(
                        "PeerCastStation/Test")),
                PcpAtom.Leaf(
                    PcpKnownAtomNames.HeloSessionId,
                    PcpWireValueCodec.EncodeGuid(RemoteSessionGuid)),
                PcpAtom.Leaf(
                    PcpKnownAtomNames.HeloVersion,
                    PcpWireValueCodec.EncodeUInt32(
                        PcpHeloCodec.LegacyServantVersion))));
        PcpAtomTreeCodec.Write(
            atoms,
            PcpAtom.Leaf(
                PcpKnownAtomNames.Okay,
                PcpWireValueCodec.EncodeUInt32(1)));
        await connection.WriteAsync(atoms.WrittenMemory, cancellationToken);
        accepted.TrySetResult();

        await releaseSource.WaitAsync(cancellationToken);
        var quit = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(
            quit,
            PcpAtom.Leaf(
                PcpKnownAtomNames.Quit,
                PcpWireValueCodec.EncodeUInt32(0)));
        try
        {
            await connection.WriteAsync(quit.WrittenMemory, cancellationToken);
        }
        catch (IOException)
        {
            // The shared coordinator may close the connection during shutdown.
        }
        catch (ObjectDisposedException)
        {
            // The shared coordinator may close the connection during shutdown.
        }
    }

    private sealed class FixedRuleSetProvider(RecordingRuleSet ruleSet) :
        IGenesisRecordingRuleSetProvider
    {
        public ValueTask<RecordingRuleSet> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ruleSet);
        }
    }

    private sealed class FixedChannelCatalog(GenesisChannelItem channel) : IGenesisChannelCatalog
    {
        private int _readCount;
        private GenesisChannelItem[] _channels = [channel];

        public int ReadCount => Volatile.Read(ref _readCount);

        public void SetChannels(IEnumerable<GenesisChannelItem> channels) =>
            Interlocked.Exchange(ref _channels, channels.ToArray());

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _readCount);
            return ValueTask.FromResult(new GenesisChannelCatalogSnapshot(
                Volatile.Read(ref _channels)));
        }
    }

    private static async Task RunPlaybackBootstrapUpstreamAsync(
        TcpTransportListener listener,
        TaskCompletionSource accepted,
        Task releaseHead,
        Task releaseSource,
        CancellationToken cancellationToken)
    {
        await using var connection = await listener.AcceptAsync(cancellationToken);
        var request = await ReadHttpRequestAsync(connection, cancellationToken);
        Assert.Contains(
            $"GET /channel/{ChannelGuid:N}",
            request,
            StringComparison.OrdinalIgnoreCase);
        await connection.WriteAsync(
            Encoding.ASCII.GetBytes(
                "HTTP/1.0 200 OK\r\n" +
                "Server: PeerCastStation/Test\r\n" +
                "Content-Type:application/x-peercast-pcp\r\n\r\n"),
            cancellationToken);

        var atoms = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(
            atoms,
            PcpAtom.Parent(
                PcpKnownAtomNames.Oleh,
                PcpAtom.Leaf(
                    PcpKnownAtomNames.HeloAgent,
                    PcpWireValueCodec.EncodeNullTerminatedUtf8(
                        "PeerCastStation/Test")),
                PcpAtom.Leaf(
                    PcpKnownAtomNames.HeloSessionId,
                    PcpWireValueCodec.EncodeGuid(RemoteSessionGuid)),
                PcpAtom.Leaf(
                    PcpKnownAtomNames.HeloVersion,
                    PcpWireValueCodec.EncodeUInt32(
                        PcpHeloCodec.LegacyServantVersion))));
        PcpAtomTreeCodec.Write(
            atoms,
            PcpAtom.Leaf(
                PcpKnownAtomNames.Okay,
                PcpWireValueCodec.EncodeUInt32(1)));
        await connection.WriteAsync(atoms.WrittenMemory, cancellationToken);
        accepted.TrySetResult();

        await releaseHead.WaitAsync(cancellationToken);
        atoms = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(
            atoms,
            PcpAtom.Parent(
                PcpKnownAtomNames.Channel,
                PcpAtom.Leaf(
                    PcpKnownAtomNames.ChannelId,
                    PcpWireValueCodec.EncodeGuid(ChannelGuid)),
                PcpAtom.Parent(
                    PcpKnownAtomNames.ChannelPacket,
                    PcpAtom.Leaf(
                        PcpKnownAtomNames.Type,
                        PcpWireValueCodec.EncodeUInt32(0x64616568)),
                    PcpAtom.Leaf(
                        PcpKnownAtomNames.Position,
                        PcpWireValueCodec.EncodeUInt32(100)),
                    PcpAtom.Leaf(
                        PcpKnownAtomNames.Data,
                        CreateFlvHeader()))));
        await connection.WriteAsync(atoms.WrittenMemory, cancellationToken);

        await releaseSource.WaitAsync(cancellationToken);
        atoms = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(
            atoms,
            PcpAtom.Leaf(
                PcpKnownAtomNames.Quit,
                PcpWireValueCodec.EncodeUInt32(0)));
        try
        {
            await connection.WriteAsync(atoms.WrittenMemory, cancellationToken);
        }
        catch (IOException)
        {
            // The shared owner may close its upstream socket during cleanup.
        }
        catch (ObjectDisposedException)
        {
            // The shared owner may close its upstream socket during cleanup.
        }
    }

    private static async Task<string> ReadHttpResponseHeaderAsync(
        TcpTransportConnection connection,
        CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var current = new byte[1];
        while (bytes.Count < 8 * 1024)
        {
            var count = await connection.ReadAsync(current, cancellationToken);
            if (count == 0)
            {
                throw new EndOfStreamException(
                    "The HTTP-FLV response ended before its header was complete.");
            }

            bytes.Add(current[0]);
            var length = bytes.Count;
            if (length >= 4 &&
                bytes[length - 4] == '\r' &&
                bytes[length - 3] == '\n' &&
                bytes[length - 2] == '\r' &&
                bytes[length - 1] == '\n')
            {
                return Encoding.ASCII.GetString(bytes.ToArray());
            }
        }

        throw new InvalidDataException(
            "The HTTP-FLV response header exceeded the test limit.");
    }

    private static async Task<byte[]> ReadExactlyAsync(
        TcpTransportConnection connection,
        int length,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await connection.ReadAsync(
                buffer.AsMemory(offset),
                cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException(
                    "The HTTP-FLV response ended before its bootstrap completed.");
            }

            offset += read;
        }

        return buffer;
    }

    private static byte[] CreateFlvHeader() =>
    [
        (byte)'F', (byte)'L', (byte)'V', 1, 5,
        0, 0, 0, 9,
        0, 0, 0, 0,
    ];

    private static async Task<string> ReadHttpRequestAsync(
        TcpTransportConnection connection,
        CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var current = new byte[1];
        while (bytes.Count < 32 * 1024)
        {
            var count = await connection.ReadAsync(
                current,
                cancellationToken);
            if (count == 0)
            {
                throw new EndOfStreamException(
                    "The upstream closed before sending its PCP request.");
            }

            bytes.Add(current[0]);
            var length = bytes.Count;
            if (length >= 4 &&
                bytes[length - 4] == '\r' &&
                bytes[length - 3] == '\n' &&
                bytes[length - 2] == '\r' &&
                bytes[length - 1] == '\n')
            {
                return Encoding.ASCII.GetString(bytes.ToArray());
            }
        }

        throw new InvalidDataException("The PCP request header exceeded its test limit.");
    }

    private sealed class NoOpPlaybackConsumer : IOwnedPcpChannelPacketConsumer
    {
        public ValueTask ConsumeAsync(
            PcpChannelPacket packet,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task PlaybackAttachmentRegistersSharedWanRouteAndUpdatesRelayTree()
    {
        await using var upstreamListener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0));
        upstreamListener.Start();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var accepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var upstreamTask = RunPlaybackBootstrapUpstreamAsync(
            upstreamListener,
            accepted,
            releaseHead.Task,
            releaseSource.Task,
            timeout.Token);

        var wanPort = ReserveFreePort();
        var wanSettings = new GenesisGuiNextConnectionPreferences
        {
            ListenPort = wanPort,
            WanRelayEnabled = true,
        };
        await using var natManager = new NatPortMappingManager(
            new NoOpNatBackend(),
            new NatPortMappingOptions { Enabled = false });
        var registry = new GenesisDownstreamRelayOwnerRegistry(
            new RelayAdmissionPolicy(
                new RelayAdmissionLimits(null, null, null),
                new UpstreamBandwidthPolicy(new UpstreamBandwidthLimits(null, null))));
        await using var publicChannels = new GenesisGuiNextPublicChannelComposition(registry);
        await using var provider = new TestSharedPublicRelayProvider(natManager);
        publicChannels.ConfigurePublic(wanSettings, provider);

        var composition = new PcpRecordingRuntimeComposition(
            Path.Combine(Path.GetTempPath(), "PeerCastNova-unified-wan-route-tests"));
        await using var runtime = new GenesisSharedPcpPlaybackRuntime(composition, publicChannels);
        var source = new DailyPcpPlaybackSource(
            IPAddress.Loopback.ToString(),
            upstreamListener.LocalEndPoint.Port,
            new ChannelId(ChannelGuid));

        GenesisSharedPcpPlaybackAttachment? attachment = null;
        try
        {
            attachment = await runtime.AttachPlaybackAsync(
                source,
                static (_, _) => new NoOpPlaybackConsumer(),
                timeout.Token);
            await accepted.Task.WaitAsync(timeout.Token);
            releaseHead.TrySetResult();

            var channelId = new ChannelId(ChannelGuid);
            var publicOwner = Assert.IsType<GenesisGuiNextPublicChannelComposition.Owner>(
                ResolvePublicOwner(publicChannels, channelId));
            await publicOwner.PublicReady.WaitAsync(timeout.Token);
            Assert.NotNull(provider.Host);
            Assert.Equal(wanPort, provider.Host!.LocalEndPoint!.Port);

            await using var peer = await ConnectDownstreamPeerAsync(wanPort, channelId, timeout.Token);
            Assert.Equal(200, peer.Handshake.HttpStatusCode);
            var headPayload = await peer.ReadPacketDataAsync(timeout.Token);
            Assert.Equal(CreateFlvHeader(), headPayload);

            var tree = registry.ReadRelayTree(channelId);
            Assert.Equal(GenesisRelayTreeState.Active, tree.State);
            Assert.Contains(tree.Nodes, node =>
                node.Kind == GenesisRelayTreeNodeKind.Downstream &&
                node.Path == GenesisRelayTreePath.SharedPublicListener);

            await peer.QuitAsync(timeout.Token);
            await WaitUntilAsync(() => registry.ActiveRelayCount == 0, timeout.Token);
        }
        finally
        {
            if (attachment is not null)
            {
                await attachment.Registration.DisposeAsync();
            }

            releaseHead.TrySetResult();
            releaseSource.TrySetResult();
            await upstreamTask.WaitAsync(timeout.Token);
            registry.Dispose();
            await composition.DisposeAsync();
        }
    }

    [Fact]
    public async Task DownstreamPeerKeepsOwnerAliveAcrossPlaybackStopAndReattach()
    {
        await using var upstreamListener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0));
        upstreamListener.Start();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var accepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionCount = 0;
        var upstreamTask = RunPlaybackBootstrapWithCountUpstreamAsync(
            upstreamListener,
            accepted,
            () => Interlocked.Increment(ref connectionCount),
            releaseHead.Task,
            releaseSource.Task,
            timeout.Token);

        var wanPort = ReserveFreePort();
        var wanSettings = new GenesisGuiNextConnectionPreferences
        {
            ListenPort = wanPort,
            WanRelayEnabled = true,
        };
        await using var natManager = new NatPortMappingManager(
            new NoOpNatBackend(),
            new NatPortMappingOptions { Enabled = false });
        var registry = new GenesisDownstreamRelayOwnerRegistry(
            new RelayAdmissionPolicy(
                new RelayAdmissionLimits(null, null, null),
                new UpstreamBandwidthPolicy(new UpstreamBandwidthLimits(null, null))));
        await using var publicChannels = new GenesisGuiNextPublicChannelComposition(registry);
        await using var provider = new TestSharedPublicRelayProvider(natManager);
        publicChannels.ConfigurePublic(wanSettings, provider);

        var composition = new PcpRecordingRuntimeComposition(
            Path.Combine(Path.GetTempPath(), "PeerCastNova-unified-downstream-retention-tests"));
        var runtime = new GenesisSharedPcpPlaybackRuntime(composition, publicChannels);
        var source = new DailyPcpPlaybackSource(
            IPAddress.Loopback.ToString(),
            upstreamListener.LocalEndPoint.Port,
            new ChannelId(ChannelGuid));
        var channelId = new ChannelId(ChannelGuid);

        try
        {
            var first = await runtime.AttachPlaybackAsync(
                source,
                static (_, _) => new NoOpPlaybackConsumer(),
                timeout.Token);
            await accepted.Task.WaitAsync(timeout.Token);
            releaseHead.TrySetResult();

            var publicOwner = ResolvePublicOwner(publicChannels, channelId)!;
            await publicOwner.PublicReady.WaitAsync(timeout.Token);

            var peer = await ConnectDownstreamPeerAsync(wanPort, channelId, timeout.Token);
            Assert.Equal(200, peer.Handshake.HttpStatusCode);
            Assert.Equal(CreateFlvHeader(), await peer.ReadPacketDataAsync(timeout.Token));

            // 視聴を終了しても下流ピアが接続中なら上流と公開リレーは維持される。
            await first.Registration.DisposeAsync();
            await Task.Delay(100, timeout.Token);
            Assert.False(first.OwnerCompletion.IsCompleted);
            Assert.Equal(1, runtime.RetainedOwnerCount);
            Assert.Equal(1, registry.ActiveRelayCount);

            // 同じチャンネルの再視聴は既存の上流接続を再利用する。
            var second = await runtime.AttachPlaybackAsync(
                source,
                static (_, _) => new NoOpPlaybackConsumer(),
                timeout.Token);
            Assert.Same(first.OwnerCompletion, second.OwnerCompletion);
            Assert.Equal(1, Volatile.Read(ref connectionCount));

            await second.Registration.DisposeAsync();
            Assert.False(first.OwnerCompletion.IsCompleted);

            // 最後の下流ピア切断でオーナーが自動停止・解放される。
            await peer.QuitAsync(timeout.Token);
            await peer.DisposeAsync();
            var result = await first.OwnerCompletion.WaitAsync(timeout.Token);
            await first.OwnerRetirement.WaitAsync(timeout.Token);
            Assert.Equal(PcpRelayCoordinatorEndReason.Stopped, result.EndReason);
            Assert.Equal(0, runtime.RetainedOwnerCount);
            Assert.Equal(0, registry.ActiveRelayCount);
            Assert.Equal(1, Volatile.Read(ref connectionCount));
        }
        finally
        {
            releaseHead.TrySetResult();
            releaseSource.TrySetResult();
            await runtime.DisposeAsync();
            await upstreamTask.WaitAsync(timeout.Token);
            registry.Dispose();
            await composition.DisposeAsync();
        }
    }

    [Fact]
    public async Task PlaybackSucceedsEvenWhenSharedWanProviderIsUnavailableOrFails()
    {
        await using var upstreamListener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0));
        upstreamListener.Start();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var accepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var upstreamTask = RunPlaybackBootstrapUpstreamAsync(
            upstreamListener,
            accepted,
            releaseHead.Task,
            releaseSource.Task,
            timeout.Token);

        var wanPort = ReserveFreePort();
        var wanSettings = new GenesisGuiNextConnectionPreferences
        {
            ListenPort = wanPort,
            WanRelayEnabled = true,
        };
        var registry = new GenesisDownstreamRelayOwnerRegistry(
            new RelayAdmissionPolicy(
                new RelayAdmissionLimits(null, null, null),
                new UpstreamBandwidthPolicy(new UpstreamBandwidthLimits(null, null))));
        await using var publicChannels = new GenesisGuiNextPublicChannelComposition(registry);
        // プロバイダ未設定で WAN 公開が失敗する状態
        publicChannels.ConfigurePublic(wanSettings, provider: null);

        var composition = new PcpRecordingRuntimeComposition(
            Path.Combine(Path.GetTempPath(), "PeerCastNova-unified-wan-fallback-tests"));
        await using var runtime = new GenesisSharedPcpPlaybackRuntime(composition, publicChannels);
        var source = new DailyPcpPlaybackSource(
            IPAddress.Loopback.ToString(),
            upstreamListener.LocalEndPoint.Port,
            new ChannelId(ChannelGuid));

        var received = new TaskCompletionSource<byte[]>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var attachment = await runtime.AttachPlaybackAsync(
            source,
            (_, _) => new RecordingPacketConsumer(received),
            timeout.Token);
        try
        {
            await accepted.Task.WaitAsync(timeout.Token);
            releaseHead.TrySetResult();
            var packetData = await received.Task.WaitAsync(timeout.Token);
            Assert.Equal(CreateFlvHeader(), packetData);
        }
        finally
        {
            await attachment.Registration.DisposeAsync();
            await attachment.OwnerCompletion.WaitAsync(timeout.Token);
            await attachment.OwnerRetirement.WaitAsync(timeout.Token);
            releaseSource.TrySetResult();
            await upstreamTask.WaitAsync(timeout.Token);
            registry.Dispose();
            await composition.DisposeAsync();
        }
    }

    private static int ReserveFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static GenesisGuiNextPublicChannelComposition.Owner? ResolvePublicOwner(
        GenesisGuiNextPublicChannelComposition publicChannels,
        ChannelId channelId)
    {
        var field = typeof(GenesisGuiNextPublicChannelComposition).GetField(
            "_current",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var current = (System.Collections.IDictionary)field!.GetValue(publicChannels)!;
        return current[channelId] as GenesisGuiNextPublicChannelComposition.Owner;
    }

    private static async Task RunPlaybackBootstrapWithCountUpstreamAsync(
        TcpTransportListener listener,
        TaskCompletionSource accepted,
        Action onAccepted,
        Task releaseHead,
        Task releaseSource,
        CancellationToken cancellationToken)
    {
        onAccepted();
        await RunPlaybackBootstrapUpstreamAsync(
            listener,
            accepted,
            releaseHead,
            releaseSource,
            cancellationToken);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, CancellationToken cancellationToken)
    {
        while (!predicate())
        {
            await Task.Delay(15, cancellationToken);
        }
    }

    private static async Task<TestDownstreamWirePeer> ConnectDownstreamPeerAsync(
        int port,
        ChannelId channel,
        CancellationToken cancellationToken,
        ushort? advertisedListenPort = null)
    {
        var sessionId = Guid.NewGuid();
        var connection = await new TcpTransportConnector(new TcpTransportOptions()).ConnectAsync("127.0.0.1", port, cancellationToken);
        var reader = PipeReader.Create(connection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            var result = await PcpRelayHandshakeClient.NegotiateAsync(
                connection,
                reader,
                "127.0.0.1",
                port,
                channel.Value,
                sessionId,
                0,
                1,
                new PcpRelayHandshakeSettings(
                    32768,
                    65536,
                    16,
                    "PeerCastNova/Test",
                    PcpHeloCodec.LegacyServantVersion,
                    advertisedListenPort),
                cancellationToken);
            return new TestDownstreamWirePeer(connection, reader, result);
        }
        catch
        {
            await reader.CompleteAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class TestDownstreamWirePeer(
        TcpTransportConnection connection,
        PipeReader reader,
        PcpRelayHandshakeResult handshake) : IAsyncDisposable
    {
        public PcpRelayHandshakeResult Handshake => handshake;

        public async Task<byte[]> ReadPacketDataAsync(CancellationToken cancellationToken)
        {
            await using var atoms = new PcpAtomPipeMessageReader(reader);
            while (true)
            {
                var atom = await atoms.ReadNextAsync(cancellationToken);
                if (atom.FindFirst(PcpKnownAtomNames.ChannelPacket)?.FindFirst(PcpKnownAtomNames.Data) is { } data)
                {
                    return data.Content.ToArray();
                }
            }
        }

        public ValueTask QuitAsync(CancellationToken cancellationToken)
        {
            var bytes = new ArrayBufferWriter<byte>();
            PcpAtomTreeCodec.Write(bytes, PcpQuitCodec.Create(1000));
            return connection.WriteAsync(bytes.WrittenMemory, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await reader.CompleteAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class RecordingPacketConsumer(TaskCompletionSource<byte[]> received) : IOwnedPcpChannelPacketConsumer
    {
        public ValueTask ConsumeAsync(PcpChannelPacket packet, CancellationToken cancellationToken)
        {
            received.TrySetResult(packet.Payload.ToArray());
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpNatBackend : INatMappingBackend
    {
        public Task<NatBackendResult> ExecuteAsync(NatMappingRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new NatBackendResult(NatBackendOutcome.Success, request.SuggestedExternalPort, 30));
    }

    private sealed class TestSharedPublicRelayProvider(NatPortMappingManager manager) : INovaSharedPublicRelayProvider, IAsyncDisposable
    {
        public GenesisSharedPublicRelay? Host { get; private set; }

        public GenesisSharedPublicRelay GetSharedRelay(
            GenesisGuiNextConnectionPreferences settings,
            Func<ChannelId, PcpRelayFanout, PcpRelayCoordinator?> resolveOwner) =>
            Host ??= new GenesisSharedPublicRelay(
                settings,
                manager,
                resolveOwner,
                new PcpWanIngressOptions { SourceAllowed = addr => addr is not null && IPAddress.IsLoopback(addr) });

        public async ValueTask DisposeAsync()
        {
            if (Host is not null)
            {
                await Host.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task ApplyLivePublicSettingsAsync_EnablesWanRouteForActivePlaybackWithoutRestart()
    {
        await using var upstreamListener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0));
        upstreamListener.Start();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var accepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var upstreamTask = RunPlaybackBootstrapUpstreamAsync(
            upstreamListener,
            accepted,
            releaseHead.Task,
            releaseSource.Task,
            timeout.Token);

        await using var natManager = new NatPortMappingManager(
            new NoOpNatBackend(),
            new NatPortMappingOptions { Enabled = false });
        var registry = new GenesisDownstreamRelayOwnerRegistry(
            new RelayAdmissionPolicy(
                new RelayAdmissionLimits(null, null, null),
                new UpstreamBandwidthPolicy(new UpstreamBandwidthLimits(null, null))));
        await using var publicChannels = new GenesisGuiNextPublicChannelComposition(registry);
        await using var provider = new TestSharedPublicRelayProvider(natManager);
        var wanPort = ReserveFreePort();
        publicChannels.ConfigurePublic(new GenesisGuiNextConnectionPreferences
        {
            ListenPort = wanPort,
            WanRelayEnabled = false,
        }, provider);

        var composition = new PcpRecordingRuntimeComposition(
            Path.Combine(Path.GetTempPath(), "PeerCastNova-live-wan-toggle-tests"));
        await using var runtime = new GenesisSharedPcpPlaybackRuntime(composition, publicChannels);
        var source = new DailyPcpPlaybackSource(
            IPAddress.Loopback.ToString(),
            upstreamListener.LocalEndPoint.Port,
            new ChannelId(ChannelGuid));

        var attachment = await runtime.AttachPlaybackAsync(
            source,
            static (_, _) => new NoOpPlaybackConsumer(),
            timeout.Token);
        try
        {
            await accepted.Task.WaitAsync(timeout.Token);
            releaseHead.TrySetResult();
            Assert.Null(provider.Host);

            var applied = await publicChannels.ApplyLivePublicSettingsAsync(new GenesisGuiNextConnectionPreferences
            {
                ListenPort = wanPort,
                WanRelayEnabled = true,
            });
            Assert.True(applied);

            var channelId = new ChannelId(ChannelGuid);
            var publicOwner = ResolvePublicOwner(publicChannels, channelId)!;
            await publicOwner.PublicReady.WaitAsync(timeout.Token);

            await using var peer = await ConnectDownstreamPeerAsync(wanPort, channelId, timeout.Token);
            Assert.Equal(200, peer.Handshake.HttpStatusCode);
            Assert.Equal(CreateFlvHeader(), await peer.ReadPacketDataAsync(timeout.Token));
            await peer.QuitAsync(timeout.Token);
            await WaitUntilAsync(() => registry.ActiveRelayCount == 0, timeout.Token);
        }
        finally
        {
            await attachment.Registration.DisposeAsync();
            await attachment.OwnerCompletion.WaitAsync(timeout.Token);
            await attachment.OwnerRetirement.WaitAsync(timeout.Token);
            releaseSource.TrySetResult();
            await upstreamTask.WaitAsync(timeout.Token);
            registry.Dispose();
            await composition.DisposeAsync();
        }
    }

    [Fact]
    public async Task WanPlaybackSendsOpenPortInUpstreamHeloAndReturnsRipInDownstreamOleh()
    {
        await using var upstreamListener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0));
        upstreamListener.Start();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var heloObserved = new TaskCompletionSource<PcpHeloInfo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var upstreamTask = Task.Run(async () =>
        {
            await using var connection = await upstreamListener.AcceptAsync(timeout.Token);
            var reader = PipeReader.Create(connection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
            try
            {
                _ = await PcpHttpHeaderBlockReader.ReadRequestAsync(reader, 32 * 1024, timeout.Token);
                await connection.WriteAsync(
                    Encoding.ASCII.GetBytes(
                        "HTTP/1.0 200 OK\r\nServer: PeerCastStation/Test\r\nContent-Type:application/x-peercast-pcp\r\n\r\n"),
                    timeout.Token);
                await using var atoms = new PcpAtomPipeMessageReader(reader);
                heloObserved.TrySetResult(PcpHeloCodec.ParseHelo(await atoms.ReadNextAsync(timeout.Token)));

                var reply = new ArrayBufferWriter<byte>();
                PcpAtomTreeCodec.Write(reply, PcpHeloCodec.CreateOleh("PeerCastStation/Test", RemoteSessionGuid));
                PcpAtomTreeCodec.Write(reply, PcpAtom.Leaf(PcpKnownAtomNames.Okay, PcpWireValueCodec.EncodeUInt32(1)));
                PcpAtomTreeCodec.Write(
                    reply,
                    PcpAtom.Parent(
                        PcpKnownAtomNames.Channel,
                        PcpAtom.Leaf(PcpKnownAtomNames.ChannelId, PcpWireValueCodec.EncodeGuid(ChannelGuid)),
                        PcpAtom.Parent(
                            PcpKnownAtomNames.ChannelPacket,
                            PcpAtom.Leaf(PcpKnownAtomNames.Type, PcpWireValueCodec.EncodeUInt32(0x64616568)),
                            PcpAtom.Leaf(PcpKnownAtomNames.Position, PcpWireValueCodec.EncodeUInt32(100)),
                            PcpAtom.Leaf(PcpKnownAtomNames.Data, CreateFlvHeader()))));
                await connection.WriteAsync(reply.WrittenMemory, timeout.Token);
                await releaseSource.Task.WaitAsync(timeout.Token);
            }
            finally
            {
                await reader.CompleteAsync();
            }
        }, timeout.Token);

        var wanPort = ReserveFreePort();
        await using var natManager = new NatPortMappingManager(
            new NoOpNatBackend(),
            new NatPortMappingOptions { Enabled = false });
        var registry = new GenesisDownstreamRelayOwnerRegistry(
            new RelayAdmissionPolicy(
                new RelayAdmissionLimits(null, null, null),
                new UpstreamBandwidthPolicy(new UpstreamBandwidthLimits(null, null))));
        await using var publicChannels = new GenesisGuiNextPublicChannelComposition(registry);
        await using var provider = new TestSharedPublicRelayProvider(natManager);
        publicChannels.ConfigurePublic(new GenesisGuiNextConnectionPreferences
        {
            ListenPort = wanPort,
            WanRelayEnabled = true,
        }, provider);

        var composition = new PcpRecordingRuntimeComposition(
            Path.Combine(Path.GetTempPath(), "PeerCastNova-helo-oleh-tests"));
        await using var runtime = new GenesisSharedPcpPlaybackRuntime(composition, publicChannels);
        var source = new DailyPcpPlaybackSource(
            IPAddress.Loopback.ToString(),
            upstreamListener.LocalEndPoint.Port,
            new ChannelId(ChannelGuid));

        var attachment = await runtime.AttachPlaybackAsync(
            source,
            static (_, _) => new NoOpPlaybackConsumer(),
            timeout.Token);
        try
        {
            var upstreamHelo = await heloObserved.Task.WaitAsync(timeout.Token);
            Assert.Equal((ushort)wanPort, upstreamHelo.OpenPort);

            var channelId = new ChannelId(ChannelGuid);
            var publicOwner = ResolvePublicOwner(publicChannels, channelId)!;
            await publicOwner.PublicReady.WaitAsync(timeout.Token);

            await using var peer = await ConnectDownstreamPeerAsync(
                wanPort,
                channelId,
                timeout.Token,
                advertisedListenPort: 27144);
            Assert.Equal(200, peer.Handshake.HttpStatusCode);
            Assert.NotNull(peer.Handshake.RemotePeer);
            Assert.Equal(IPAddress.Loopback, peer.Handshake.RemotePeer!.RemoteAddress);
            Assert.Equal((ushort)27144, peer.Handshake.RemotePeer.RemotePort);

            await peer.QuitAsync(timeout.Token);
            await WaitUntilAsync(() => registry.ActiveRelayCount == 0, timeout.Token);
        }
        finally
        {
            await attachment.Registration.DisposeAsync();
            await attachment.OwnerCompletion.WaitAsync(timeout.Token);
            await attachment.OwnerRetirement.WaitAsync(timeout.Token);
            releaseSource.TrySetResult();
            await upstreamTask.WaitAsync(timeout.Token);
            registry.Dispose();
            await composition.DisposeAsync();
        }
    }
}
